"""Resize guide follows pointer presence without losing table selection.
Run: python tests/editor-table-resize-hover-087-browser.py
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
        browser = await pw.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                           args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            for host_handles in (False, True):
                page = await browser.new_page(viewport={'width': 1160, 'height': 900})
                errors = []
                page.on('pageerror', lambda exc: errors.append(str(exc)))
                await page.set_content(HTML.read_text())
                if host_handles:
                    await page.evaluate('''() => document.querySelector('#browser-test').insertAdjacentHTML('beforeend',
                      '<button type="button" class="az-azunt-editor-table-move-handle" data-table-move-handle hidden>✥</button>'+ 
                      '<button type="button" class="az-azunt-editor-table-column-handle" data-table-column-handle hidden></button>'+ 
                      '<div class="az-azunt-editor-table-drop-marker" data-table-drop-marker hidden></div>')''')
                await page.add_style_tag(content=CSS.read_text())
                await page.add_script_tag(content=JS.read_text())
                root = page.locator('#browser-test')
                canvas = root.locator('.az-azunt-editor-canvas')
                grip = root.locator('[data-table-column-handle]')
                move = root.locator('[data-table-move-handle]')
                cell = canvas.locator('td').first
                original = '<p>Before table</p><table><tr><td>Alpha</td><td>Beta</td><td>Gamma</td></tr><tr><td>Delta</td><td>Epsilon</td><td>Zeta</td></tr></table><p>After table</p>'
                await page.evaluate('(html) => AzuntEditor.setHtml("browser-test", html)', original)
                await cell.click()
                before = await page.evaluate('AzuntEditor.getHtml("browser-test")')
                assert await grip.is_visible()
                await canvas.locator('p').last.hover()
                assert not await grip.is_visible(), 'resize guide persists outside table inside canvas'
                assert await move.is_visible(), 'table selection/move grip must survive hover exit'
                # Overlay updates must not resurrect a hidden guide.
                await page.evaluate('window.dispatchEvent(new Event("resize"))')
                assert not await grip.is_visible()
                assert before == await page.evaluate('AzuntEditor.getHtml("browser-test")')
                await cell.hover()
                assert await grip.is_visible(), 'same table must show guide again without click'
                await page.mouse.move(2, 2)
                assert not await grip.is_visible(), 'guide persists outside editor'
                await cell.hover()
                assert await grip.is_visible()
                await page.evaluate('''() => document.querySelector('#browser-test .az-azunt-editor-canvas td')
                    .dispatchEvent(new PointerEvent('pointerout',{bubbles:true,pointerType:'mouse',relatedTarget:null}))''')
                assert not await grip.is_visible(), 'window exit should hide guide'
                box = await cell.bounding_box()
                await page.mouse.move(box['x'] + 10, box['y'] + 10)
                assert await grip.is_visible()
                print('PASS: hover exit, reentry, window exit, selection and HTML preservation', host_handles)

                # Real resize must continue outside the table while pressed; it must
                # commit on release and then hide immediately at the outside pointer.
                b = await grip.bounding_box()
                await page.mouse.move(b['x'] + 5, b['y'] + 10)
                await page.mouse.down()
                await page.mouse.move(b['x'] + 42, b['y'] - 20, steps=5)
                assert await grip.is_visible(), 'active drag lost its guide'
                await page.mouse.up()
                assert not await grip.is_visible(), 'outside pointerup should hide guide'
                resized = await page.evaluate('AzuntEditor.getHtml("browser-test")')
                assert 'table-layout: fixed' in resized and 'Alpha' in resized
                await cell.click()
                await page.keyboard.press('Control+z')
                assert 'table-layout: fixed' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
                print('PASS: resize outside table, release, persisted widths and undo', host_handles)

                # Losing capture unexpectedly rolls back partial width changes.
                await cell.click()
                b = await grip.bounding_box()
                await page.mouse.move(b['x'] + 5, b['y'] + 10)
                await page.mouse.down()
                await page.mouse.move(b['x'] + 42, b['y'] + 10, steps=5)
                await grip.evaluate('''el => { for (let id=1;id<10;id++) if(el.hasPointerCapture(id)) el.releasePointerCapture(id); }''')
                await page.mouse.move(b['x'] + 43, b['y'] + 10)
                await page.mouse.up()
                assert 'table-layout: fixed' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
                await canvas.locator('p').last.hover()
                assert not await grip.is_visible()
                await cell.click()
                assert await grip.is_visible(), 'capture loss blocked next gesture'
                print('PASS: unexpected capture loss rolls back and next gesture works', host_handles)

                # Stationary mouse while the page moves: re-evaluate pointer/table overlap.
                await page.evaluate('document.body.style.paddingBottom = "1400px"')
                await cell.hover()
                await page.evaluate('window.scrollBy(0, 450)')
                await page.wait_for_function('document.querySelector("[data-table-column-handle]").hidden')
                await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
                assert not await grip.count() or not await grip.is_visible()
                assert 'data-table-column-handle' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
                print('PASS: scroll and view mode hide guide; no guide markup in document', host_handles)
                assert not errors, errors
                await page.close()
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
