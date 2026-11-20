"""0.7.0 table editing: content preservation, caret, keyboard, undo/redo.
Run: python tests/editor-table-stability-070-browser.py (Playwright + Chromium)
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = ROOT / 'tests/editor-adaptive-browser.html'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'


async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(
            executable_path='/usr/bin/chromium', headless=True,
            args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page()
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            await page.set_content(HTML.read_text())
            await page.add_style_tag(content=CSS.read_text())
            await page.add_script_tag(content=JS.read_text())
            canvas = page.locator('#browser-test .az-azunt-editor-canvas')
            editor = page.locator('#browser-test')
            # Toolbar's delegated click listener can accept normal table commands.
            await page.evaluate('''() => {
              const toolbar = document.querySelector('#browser-test .az-azunt-editor-toolbar');
              for (const name of ['table-merge-right','table-split','table-row-before','table-row-after',
                                   'table-row-delete','table-col-before','table-col-after','table-col-delete',
                                   'table-header-toggle','table-delete']) {
                const button = document.createElement('button');
                button.type = 'button'; button.dataset.command = name;
                button.textContent = name; toolbar.appendChild(button);
              }
            }''')

            async def load(html):
                await page.evaluate('(value) => AzuntEditor.setHtml("browser-test", value)', html)

            async def saved():
                return await page.evaluate('AzuntEditor.getHtml("browser-test")')

            async def command(name):
                await editor.locator(f'.az-azunt-editor-toolbar > button[data-command="{name}"]').click()

            async def cursor(row, col, offset=0):
                return await page.evaluate('''([row, col, offset]) => {
                  const canvas = document.querySelector('#browser-test .az-azunt-editor-canvas');
                  const cell = canvas.querySelectorAll('tr')[row].cells[col];
                  const node = cell.firstChild;
                  const range = document.createRange();
                  if (node.nodeType === Node.TEXT_NODE) range.setStart(node, Math.min(offset,node.length));
                  else range.setStart(cell, 0);
                  range.collapse(true);
                  canvas.focus();
                  const s = window.getSelection();s.removeAllRanges();s.addRange(range);
                  canvas.dispatchEvent(new KeyboardEvent('keyup', { bubbles:true, key:'ArrowRight' }));
                }''', [row, col, offset])

            async def current_cell():
                return await page.evaluate('''() => {
                  const root = document.querySelector('#browser-test .az-azunt-editor-canvas');
                  const node = window.getSelection()?.anchorNode;
                  const el = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
                  const cell = el?.closest('td,th');
                  return cell && root.contains(cell) ? {
                    row: Array.from(cell.closest('table').rows).indexOf(cell.closest('tr')),
                    col: Array.from(cell.parentElement.cells).indexOf(cell),
                    tag: cell.tagName,
                    text: cell.textContent
                  } : null;
                }''')

            # 1. A form input alone in the right cell used to be silently deleted.
            await load('<table><tbody><tr><td>Left</td><td><input type="checkbox" checked></td><td>Third</td></tr>'
                       '<tr><td>A</td><td>B</td><td>C</td></tr></tbody></table>')
            await cursor(0, 0)
            await command('table-merge-right')
            merged = await saved()
            assert 'colspan="2"' in merged, merged
            assert '<input type="checkbox" checked' in merged, merged
            assert 'Third' in merged and 'Left' in merged, merged
            assert (await current_cell())['col'] == 0, await current_cell()
            await editor.locator('[data-quick-command="undo"]').click()
            assert await canvas.locator('tr').first.locator('td').count() == 3
            await editor.locator('[data-quick-command="redo"]').click()
            assert 'checked' in await saved()
            await command('table-split')
            assert await canvas.locator('tr').first.locator('td').count() == 3
            assert 'checked' in await saved()
            print('PASS: merged input retained through Undo, Redo and Split')

            # 2. Empty placeholders do not eat content and other inline elements survive.
            await load('<table><tr><td><br></td><td><input type="radio" checked></td></tr></table>')
            await cursor(0, 0)
            await command('table-merge-right')
            assert (await saved()).count('type="radio"') == 1 and 'checked' in await saved()
            await load('<table><tr><td>L</td><td><hr></td></tr></table>')
            await cursor(0, 0)
            await command('table-merge-right')
            assert '<hr>' in await saved()
            print('PASS: radio and structural content are not dropped')

            # 3. Selection updated by keyboard (rather than mouse click) selects
            #    the correct row. New cells retain supported table borders.
            await load('<table><tbody><tr><td>AA</td><td>AB</td></tr><tr><td>BA</td><td style="border-color:#aa2211;border-width:2px;border-style:solid">BB</td></tr><tr><td>CA</td><td>CB</td></tr></tbody></table>')
            await cursor(1, 1, 1)
            await command('table-row-after')
            assert await canvas.locator('tr').count() == 4
            assert await canvas.locator('tr').nth(2).locator('td').count() == 2
            assert 'CA' in await canvas.locator('tr').nth(3).inner_text()
            assert (await current_cell())['row'] == 2, await current_cell()
            assert 'border-width: 2px' in await canvas.locator('tr').nth(2).locator('td').nth(1).get_attribute('style')
            await command('table-col-before')
            assert await canvas.locator('tr').first.locator('td').count() == 3
            assert (await current_cell())['col'] == 1, await current_cell()
            # Existing content remains present at all stages.
            current_html = await saved()
            assert all(label in current_html for label in ['AA','AB','BA','BB','CA','CB']), current_html
            print('PASS: insert follows keyboard caret, keeps styles and old data')

            # 4. Header conversion keeps the current caret in the replacement
            #    node, so typing and Undo work without another click.
            await cursor(1, 0)
            await command('table-header-toggle')
            assert (await current_cell())['tag'] == 'TH', await current_cell()
            await page.keyboard.type('Z')
            assert 'Z' in await canvas.locator('tr').nth(1).locator('th').first.inner_text()
            await editor.locator('[data-quick-command="undo"]').click()
            assert 'Z' not in await canvas.locator('tr').nth(1).locator('th').first.inner_text()
            print('PASS: header toggle, typing and Undo preserve selection')

            # 5. Deleting columns/rows must put the caret in a surviving cell.
            await cursor(1, 1)
            await command('table-col-delete')
            assert (await current_cell())['row'] == 1
            await command('table-row-delete')
            assert await canvas.locator('tr').count() == 3
            assert await current_cell() is not None
            print('PASS: row/column deletion maintains valid caret')

            # 6. Tab and Shift+Tab navigate existing cells without inserting
            #    a new table row or overriding normal boundary focus.
            await load('<table><tr><td>A</td><td>B</td></tr><tr><td>C</td><td>D</td></tr></table>')
            await cursor(0, 0)
            await page.keyboard.press('Tab')
            assert (await current_cell()) == {'row': 0, 'col': 1, 'tag': 'TD', 'text': 'B'}, await current_cell()
            await page.keyboard.press('Tab')
            assert (await current_cell())['row'] == 1
            await page.keyboard.press('Shift+Tab')
            assert (await current_cell())['row'] == 0
            assert await canvas.locator('tr').count() == 2
            print('PASS: Tab/Shift+Tab navigates table cells')

            # 7. Refuse potentially destructive dimension changes while merged;
            #    after splitting the same change is allowed.
            await cursor(0, 0)
            await command('table-merge-right')
            snapshot = await saved()
            await command('table-row-before')
            assert await saved() == snapshot, 'Operation on merged table should be refused'
            await command('table-split')
            await command('table-row-before')
            assert await canvas.locator('tr').count() == 3
            print('PASS: merged-table guard remains intact')

            # 8. Deleting the table with content before/after leaves an editable
            #    selection in the document and does not destroy surrounding data.
            await load('<p>Before</p><table><tr><td>Delete</td></tr></table><p>After</p>')
            await cursor(0, 0)
            await command('table-delete')
            assert await canvas.locator('table').count() == 0
            assert '<p>Before</p>' in await saved() and '<p>After</p>' in await saved()
            assert await page.evaluate('''() => {
              const canvas=document.querySelector('#browser-test .az-azunt-editor-canvas');
              return canvas.contains(window.getSelection().anchorNode);
            }''')
            print('PASS: table deletion retains surrounding document and valid selection')

            # 9. Multiple tables: a caret in the second table must not alter
            #    a previously active cell in the first table.
            await load('<table><tr><td>First A</td><td>First B</td></tr></table>'
                       '<p>Between</p><table><tr><td>Second A</td><td>Second B</td></tr></table>')
            await page.evaluate('''() => {
              const canvas=document.querySelector('#browser-test .az-azunt-editor-canvas');
              const node=canvas.querySelectorAll('table')[1].rows[0].cells[0].firstChild;
              const range=document.createRange();range.setStart(node,2);range.collapse(true);
              canvas.focus();const s=window.getSelection();s.removeAllRanges();s.addRange(range);
              canvas.dispatchEvent(new KeyboardEvent('keyup',{bubbles:true,key:'ArrowRight'}));
            }''')
            await command('table-merge-right')
            assert await canvas.locator('table').first.locator('[colspan]').count() == 0
            assert await canvas.locator('table').nth(1).locator('td[colspan="2"]').count() == 1
            print('PASS: table command targets the actual caret table')

            # 10. Edit/View toggling keeps ordinary table HTML unchanged and
            #     read-only mode cannot run the toolbar's destructive actions.
            before_view = await saved()
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            assert await canvas.get_attribute('contenteditable') == 'false'
            assert await saved() == before_view
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert await saved() == before_view
            print('PASS: table editing guarded in View, saved HTML survives mode switching')

            assert not errors, errors
            print('PASS: no page errors')
        finally:
            await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
