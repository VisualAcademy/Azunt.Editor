"""Blazor prerender/hydration regression: no early DOM changes, commands bind once.

Run: python tests/editor-blazor-hydration-browser.py
This simulates JS timing, not an actual .NET Server circuit.
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
FIXTURE = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')


async def main():
    # The fixture intentionally has both automatic MVC and manually bound
    # Blazor markup. Script loading must not mutate prerendered Blazor HTML.
    html = FIXTURE.replace('id="browser-test"', 'id="blazor-test" data-azunt-mount="manual"')
    second = FIXTURE[FIXTURE.index('<div class="az-azunt-editor"'):FIXTURE.index('</body>')].replace('id="browser-test"', 'id="mvc-test"')
    html = html.replace('</body>', second + '</body>')
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page(viewport={'width': 1150, 'height': 780})
            errors = []
            page.on('pageerror', lambda exc: errors.append(str(exc)))
            await page.set_content(html)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            # DOMContentLoaded-like automatic mount on a page already loaded.
            await page.evaluate('AzuntEditor.mountAll()')
            assert await page.locator('#blazor-test .az-azunt-editor-canvas').get_attribute('contenteditable') == 'true'
            assert await page.locator('#blazor-test [data-menu-panel]').get_attribute('id') is None, 'Prerendered markup was mutated too early'
            assert await page.locator('#mvc-test [data-menu-panel]').get_attribute('id') == 'mvc-test-command-menu', 'MVC must still auto-mount'

            await page.evaluate('''() => {
                window.commandCalls = [];
                const dotNet = {invokeMethodAsync: (method, ...params) => {
                    window.commandCalls.push([method, ...params]);
                    return Promise.resolve();
                }};
                window.mockDotNet = dotNet;
                // Blazor may replace the prerendered root before its first
                // OnAfterRenderAsync. Mount must bind to the new DOM.
                const el = document.getElementById('blazor-test');
                el.replaceWith(el.cloneNode(true));
                AzuntEditor.mount('blazor-test', dotNet);
                AzuntEditor.setCommands('blazor-test', [
                    {id:'downloadPdf',label:'Download PDF',group:'Export'},
                    {id:'downloadWord',label:'Download Word',group:'Export'},
                    {id:'downloadExcel',label:'Download Excel',group:'Export'}
                ]);
            }''')
            export = page.locator('#blazor-test [data-azunt-menu="export"]')
            canvas = page.locator('#blazor-test .az-azunt-editor-canvas')
            for i in range(4):
                await export.click()
                buttons = page.locator('#blazor-test [data-menu-panel] button[data-custom-command]')
                assert await buttons.all_inner_texts() == ['Download PDF', 'Download Word', 'Download Excel']
                await buttons.first.click()
                await page.evaluate('AzuntEditor.setMode("blazor-test", "View")')
                assert await page.locator('#blazor-test .az-azunt-editor-toolbar').is_hidden()
                await page.evaluate('AzuntEditor.setMode("blazor-test", "Edit")')
                assert await canvas.get_attribute('contenteditable') == 'true'
            calls = await page.evaluate('window.commandCalls.filter(x=>x[0]==="OnCustomCommand")')
            assert calls == [['OnCustomCommand','downloadPdf']]*4, calls
            # Mount should remain idempotent even when OnAfterRender repeats.
            await page.evaluate('''() => {
                AzuntEditor.mount('blazor-test', window.mockDotNet);
                AzuntEditor.setCommands('blazor-test', [
                  {id:'downloadPdf',label:'Download PDF',group:'Export'},
                  {id:'downloadWord',label:'Download Word',group:'Export'},
                  {id:'downloadExcel',label:'Download Excel',group:'Export'}
                ]);
            }''')
            await export.click()
            assert await page.locator('#blazor-test [data-menu-panel] button[data-custom-command]').count() == 3
            assert not errors, errors
            print('PASS: Blazor prerender mount isolation, MVC auto-mount, repeated modes, export commands')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
