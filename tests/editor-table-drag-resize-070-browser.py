"""Table move grip and column boundary resize; Chromium regression tests.
Run: python tests/editor-table-drag-resize-070-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = ROOT / 'tests/editor-adaptive-browser.html'
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'

async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
        try:
            page = await browser.new_page(viewport={'width':1160,'height':900})
            errors = []
            page.on('pageerror', lambda exc: errors.append(str(exc)))
            await page.set_content(HTML.read_text())
            await page.add_style_tag(content=CSS.read_text())
            await page.add_script_tag(content=JS.read_text())
            editor=page.locator('#browser-test')
            canvas=editor.locator('.az-azunt-editor-canvas')
            move=editor.locator('.az-azunt-editor-table-move-handle')
            resize=editor.locator('.az-azunt-editor-table-column-handle')
            html='<p>Before</p><table><tbody><tr><td>A1</td><td>B1</td><td>C1</td></tr><tr><td><input type="checkbox" checked></td><td>B2</td><td>C2</td></tr></tbody></table><p>After</p>'
            await page.evaluate('(h) => AzuntEditor.setHtml("browser-test", h)',html)
            await canvas.locator('td').first.click()
            assert await move.is_visible(), 'move grip missing'
            assert not await resize.is_visible(), 'idle resize hit area should be hidden'
            before=await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert before.index('Before') < before.index('<table') < before.index('After')
            move_box=await move.bounding_box()
            p_box=await canvas.locator('p').last.bounding_box()
            await page.mouse.move(move_box['x']+move_box['width']/2,move_box['y']+move_box['height']/2)
            await page.mouse.down()
            await page.mouse.move(p_box['x']+12,p_box['y']+p_box['height']*.8,steps=6)
            await page.mouse.up()
            saved=await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert saved.index('After') < saved.index('<table'), saved
            assert 'checked' in saved and 'A1' in saved and 'C2' in saved
            print('PASS: moved whole table after paragraph; all cell content preserved')
            await page.evaluate('''() => { const c = document.querySelector('#browser-test .az-azunt-editor-canvas'); c.focus();
              const e = new KeyboardEvent('keydown',{key:'z',ctrlKey:true,bubbles:true,cancelable:true}); c.dispatchEvent(e);
            }''')
            saved=await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert saved.index('<table') < saved.index('After'),saved
            print('PASS: undo restores original table location')
            await canvas.locator('td').first.click()
            cell_box=await canvas.locator('td').first.bounding_box()
            await page.mouse.move(cell_box['x']+cell_box['width']-2,cell_box['y']+cell_box['height']/2)
            resize_box=await resize.bounding_box()
            assert resize_box and resize_box['height']>20,resize_box
            widths_before=await canvas.locator('tr').first.locator('td').evaluate_all('(cells) => cells.map(c => c.getBoundingClientRect().width)')
            await page.mouse.move(resize_box['x']+5,resize_box['y']+15)
            await page.mouse.down()
            await page.mouse.move(resize_box['x']+38,resize_box['y']+15,steps=5)
            await page.mouse.up()
            widths_after=await canvas.locator('tr').first.locator('td').evaluate_all('(cells) => cells.map(c => c.getBoundingClientRect().width)')
            print('widths',widths_before,widths_after)
            assert widths_after[0] > widths_before[0]+14,(widths_before,widths_after)
            assert widths_after[1] < widths_before[1]-14
            saved=await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert 'table-layout: fixed' in saved and 'width:' in saved, saved
            print('PASS: column drag adjusts adjacent widths and persists safe inline styles')
            await page.evaluate('AzuntEditor.setMode("browser-test","View")')
            assert not await editor.locator('.az-azunt-editor-table-move-handle').count(), 'view must have no drag tools'
            assert 'width:' in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            await page.evaluate('AzuntEditor.setMode("browser-test","Edit")')
            assert 'width:' in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            print('PASS: Edit/View roundtrip keeps cell widths and removes editing controls')
            await page.evaluate('''() => { const c=document.querySelector('#browser-test .az-azunt-editor-canvas'); c.focus();c.dispatchEvent(new KeyboardEvent('keydown',{key:'z',ctrlKey:true,bubbles:true,cancelable:true})); }''')
            assert 'table-layout: fixed' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            print('PASS: column drag is undoable')
            await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Top</p><table><tr><td colspan=2>Merged</td><td>X</td></tr></table><p>Bottom</p>")')
            await canvas.locator('td').first.click()
            assert await move.is_visible()
            assert not await resize.is_visible(), 'merged column grip should be disabled'
            print('PASS: merged tables can move but column resizing is disabled')
            assert 'az-azunt-editor-table-move-handle' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            await page.evaluate('''() => {
              const root=document.querySelector('#browser-test');
              const b=document.createElement('button'); b.type='button'; b.dataset.quickCommand='source';
              root.querySelector('.az-azunt-editor-toolbar').append(b); b.click();
            }''')
            assert not await move.is_visible() and not await resize.is_visible()
            await page.evaluate('''() => document.querySelector('#browser-test [data-quick-command="source"]').click()''')
            print('PASS: source mode hides controls without introducing saved UI markup')
            assert not errors, errors
        finally:
            await browser.close()

if __name__=='__main__':
    asyncio.run(main())
