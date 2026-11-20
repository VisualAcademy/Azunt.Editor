"""0.8.0 toolbar split-button layout + command regressions.
Run: python tests/editor-toolbar-split-080-browser.py
The browser fixture mirrors EditorMarkup; static checks verify the source contract.
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CS = (ROOT / 'src/Azunt.Editor/Rendering/EditorMarkup.cs').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')

# Ensure both host rendering paths share common markup and the Compact Insert group
# (not a pair of ungrouped controls trailing the Tools group) owns all three.
assert 'TableSplit(compact: true);\n                FormSplit(compact: true);\n                CodeSplit(compact: true);' in CS
assert 'FormSplit(compact: false);' in CS and 'CodeSplit(compact: false);' in CS
assert 'az-azunt-editor-tool-split' in CS and 'az-azunt-editor-tool-split' in CSS
assert CS.count('data-azunt-menu=\\"tablepicker') == 1


def split(name, compact):
    cmd = 'data-quick-command' if compact else 'data-command'
    if name == 'table':
        return f'''<span class="az-azunt-editor-tool-split az-azunt-editor-table-split" role="group" aria-label="Insert table">
          <button class="az-azunt-editor-button az-azunt-editor-table-primary" type="button" {cmd}="table" title="Insert 2 × 2 table">▦</button>
          <button class="az-azunt-editor-button az-azunt-editor-table-dropdown" type="button" data-azunt-menu="tablepicker" aria-expanded="false" aria-haspopup="dialog">▾</button></span>'''
    if name == 'form':
        return f'''<span class="az-azunt-editor-tool-split az-azunt-editor-form-split" role="group" aria-label="Insert HTML form control">
          <button class="az-azunt-editor-button az-azunt-editor-form-primary" type="button" {cmd}="form-checkbox-checked">☑</button>
          <button class="az-azunt-editor-button az-azunt-editor-form-dropdown" type="button" data-azunt-menu="controls" aria-expanded="false" aria-haspopup="dialog">▾</button></span>'''
    return f'''<span class="az-azunt-editor-tool-split az-code-toolbar" role="group" aria-label="Insert code block">
          <button class="az-azunt-editor-button az-code-toolbar-primary" type="button" {cmd}="code-block">&lt;/&gt;</button>
          <button class="az-azunt-editor-button az-code-toolbar-dropdown" type="button" data-azunt-menu="codepicker" aria-expanded="false" aria-haspopup="dialog">▾</button></span>'''


def fixture():
    compact = ''.join(split(name, True) for name in ('table', 'form', 'code'))
    classic = ''.join(split(name, False) for name in ('table', 'form', 'code'))
    html = HTML.replace('<button data-quick-command="table" type="button">table</button>', compact)
    html = html.replace('<button data-command="table" type="button">Table</button>', classic)
    # Match the source's Compact grouping of Insert commands as one labeled group.
    html = html.replace('<button data-quick-command="link" type="button">link</button>',
        '<span class="az-azunt-editor-quick-group az-azunt-editor-quick-group-optional"><span class="az-azunt-editor-quick-label">Insert</span><span class="az-azunt-editor-quick-controls"><button data-quick-command="link" class="az-azunt-editor-button" type="button">link</button>')
    html = html.replace('<span data-quick-custom=""></span>', '</span></span><span data-quick-custom=""></span>')
    html = html.replace('<div class="az-azunt-editor-canvas"', '''<div class="az-code-panel" data-code-panel hidden>
       <select data-code-language><option value="csharp">C#</option><option value="sql">SQL</option></select>
       <textarea data-code-text></textarea><button data-code-apply>Apply</button><button data-code-cancel>Cancel</button>
       </div><div class="az-azunt-editor-canvas"''')
    return html


async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                           args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            for scheme in ('light', 'dark'):
                page = await browser.new_page(viewport={'width': 1280, 'height': 780}, color_scheme=scheme)
                errors = []
                page.on('pageerror', lambda err: errors.append(str(err)))
                await page.set_content(fixture())
                await page.add_style_tag(content=CSS)
                await page.add_script_tag(content=JS)
                root = page.locator('#browser-test')
                compact = root.locator('[data-compact-quick]')
                insert = compact.locator('.az-azunt-editor-quick-group:has(.az-azunt-editor-quick-label:text-is("Insert"))')
                assert await insert.count() == 1
                for name in ('table', 'form', 'code'):
                    assert await insert.locator({'table':'.az-azunt-editor-table-split','form':'.az-azunt-editor-form-split','code':'.az-code-toolbar'}[name]).count() == 1
                splits = insert.locator('.az-azunt-editor-tool-split')
                boxes = [await splits.nth(i).bounding_box() for i in range(3)]
                assert all(abs(b['height'] - 34) <= 1 for b in boxes), boxes
                assert max(b['y'] for b in boxes) - min(b['y'] for b in boxes) < 2, boxes
                for i in range(3):
                    outer = splits.nth(i)
                    buttons = outer.locator('button')
                    first, arrow = await buttons.nth(0).bounding_box(), await buttons.nth(1).bounding_box()
                    assert abs((first['x'] + first['width']) - arrow['x']) < 1, (first, arrow)
                    assert abs(first['y'] - arrow['y']) <= 1 and abs(first['height'] - arrow['height']) <= 1
                    assert await outer.evaluate('(node) => getComputedStyle(node).borderTopStyle') == 'solid'
                    assert await buttons.nth(1).evaluate('(node) => getComputedStyle(node).borderLeftStyle') == 'solid'

                menu = root.locator('[data-menu-panel]')
                canvas = root.locator('.az-azunt-editor-canvas')
                # Dropdown remains anchored, and quick table button still inserts 2x2.
                await insert.locator('[data-azunt-menu="tablepicker"]').click()
                assert await menu.locator('[data-table-rows][data-table-columns]').count() == 100
                await menu.locator('[data-table-rows="2"][data-table-columns="3"]').click()
                assert await canvas.locator('table tr').count() == 2
                assert await canvas.locator('table td').count() == 6
                await page.evaluate('AzuntEditor.setHtml("browser-test", "")')
                await insert.locator('[data-quick-command="table"]').click()
                assert await canvas.locator('table td').count() == 4
                # Checkbox split and code dropdown still dispatch correctly.
                await page.evaluate('AzuntEditor.setHtml("browser-test", "")')
                await insert.locator('[data-quick-command="form-checkbox-checked"]').click()
                assert await canvas.locator('input[type="checkbox"][checked]').count() == 1
                await insert.locator('[data-azunt-menu="controls"]').click()
                assert await menu.locator('[data-command="form-radio"]').count() == 1
                await page.keyboard.press('Escape')
                await insert.locator('[data-azunt-menu="codepicker"]').click()
                assert await menu.locator('[data-command="code-block-sql"]').count() == 1
                await menu.locator('[data-command="code-block-sql"]').click()
                assert await root.locator('[data-code-panel]').is_visible()
                await root.locator('[data-code-cancel]').click()
                # Switching to classic retains one connected split group for each tool.
                await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Classic")')
                classics = root.locator('[data-classic-groups] .az-azunt-editor-tool-split')
                assert await classics.count() == 3
                cboxes = [await classics.nth(i).bounding_box() for i in range(3)]
                assert all(abs(b['height'] - 34) <= 1 for b in cboxes), cboxes
                await classics.nth(0).locator('[data-azunt-menu="tablepicker"]').click()
                assert await menu.is_visible()
                await page.keyboard.press('Escape')
                assert not errors, (scheme, errors)
                if scheme == 'light':
                    await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Compact")')
                    await page.screenshot(path='/mnt/data/Azunt.Editor.0.8.0-toolbar-preview.png')
                await page.close()
            print('PASS: Insert grouping, unified 34px split controls, Table/Form/Code commands, menu, Classic, dark mode')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
