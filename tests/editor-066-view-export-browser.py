"""0.6.6 Edit/View/Export lifecycle and pending JS->Blazor changes regression.
Run: python tests/editor-066-view-export-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
FIXTURE = (ROOT / 'tests/editor-adaptive-browser.html').read_text()


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page(viewport={'width': 1200, 'height': 830})
            errors = []
            page.on('pageerror', lambda err: errors.append(str(err)))
            await page.set_content(FIXTURE)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            await page.evaluate('''() => {
              window.callbacks = [];
              AzuntEditor.mount('browser-test', { invokeMethodAsync:(method,...args) => {
                window.callbacks.push([method,...args]); return Promise.resolve();
              }});
              AzuntEditor.setCommands('browser-test', [
                {id:'downloadPdf', label:'Download PDF', group:'Export'},
                {id:'downloadWord', label:'Download Word', group:'Export'},
                {id:'downloadExcel', label:'Download Excel', group:'Export'}
              ]);
            }''')
            canvas = page.locator('#browser-test .az-azunt-editor-canvas')
            export = page.locator('#browser-test [data-compact-menu] [data-azunt-menu="export"]')
            await export.click()
            assert await page.locator('#browser-test [data-menu-panel] [data-custom-command]').count() == 3
            await page.keyboard.press('Escape')
            # Edit immediately followed by View: pending debounced Blazor sync must be sent.
            await page.evaluate('''() => {
              const c=document.querySelector('#browser-test .az-azunt-editor-canvas');
              c.innerHTML='<p>Changed just before mode switch</p>';
              c.dispatchEvent(new Event('input',{bubbles:true}));
              AzuntEditor.setMode('browser-test','View');
            }''')
            calls = await page.evaluate('window.callbacks')
            assert len([c for c in calls if c[0] == 'OnHtmlChanged' and c[1] == '<p>Changed just before mode switch</p>']) == 1, calls
            await page.wait_for_timeout(300)
            calls2 = await page.evaluate('window.callbacks')
            assert calls == calls2, 'No late sync after unmount'
            assert await canvas.get_attribute('contenteditable') == 'false'
            assert await page.locator('#browser-test [data-azunt-value]').is_disabled()
            assert await page.locator('#browser-test .az-azunt-editor-toolbar').is_hidden()
            assert 'Changed just before mode switch' in await canvas.inner_text()
            # Register a new command while viewer does not initialize toolbar listeners.
            await page.evaluate('''() => AzuntEditor.setCommands('browser-test', [
                {id:'downloadPdf', label:'Download PDF', group:'Export'},
                {id:'downloadWord', label:'Download Word', group:'Export'},
                {id:'downloadExcel', label:'Download Excel', group:'Export'},
                {id:'exportHtml', label:'Export HTML', group:'Export'}
              ])''')
            for _ in range(3):
                await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
                assert await canvas.get_attribute('contenteditable') == 'true'
                assert await page.locator('#browser-test [data-azunt-value]').is_enabled()
                await export.click()
                assert await page.locator('#browser-test [data-menu-panel] [data-custom-command]').count() == 4
                await page.locator('#browser-test [data-menu-panel] [data-custom-command="downloadPdf"]').click()
                await page.keyboard.press('Escape')
                await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
                assert await page.locator('#browser-test .az-azunt-editor-toolbar').is_hidden()
            invokes = await page.evaluate('window.callbacks.filter(c => c[0] === "OnCustomCommand")')
            assert invokes == [['OnCustomCommand', 'downloadPdf']] * 3, invokes
            # Protocol-relative image sources must not bypass the sanitizer.
            assert await page.evaluate('''() => AzuntEditor.sanitizeHtml(
              '<p>Link <a href="//evil.example">bad</a></p>' +
              '<img src="//evil.example/pic.png" onerror="alert(1)">')''') == '<p>Link <a>bad</a></p>'
            # Unmount/mount is idempotent; cleanup leaves a working editor.
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            await page.evaluate('''() => { AzuntEditor.unmount('browser-test'); AzuntEditor.mount('browser-test'); }''')
            assert await canvas.get_attribute('contenteditable') == 'true'
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            assert await page.locator('#browser-test .az-azunt-editor-toolbar').is_hidden()
            assert not errors, errors
            print('PASS: export commands, pending Blazor sync, repeated mode cycles, toolbar disposal')
        finally:
            await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
