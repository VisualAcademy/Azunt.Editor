"""Standalone Chromium check of compact/classic toolbar and extensible commands.
Run: python tests/editor-adaptive-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
FIXTURE = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        page = await browser.new_page(viewport={'width':1260,'height':820})
        errors=[]
        page.on('pageerror', lambda err: errors.append(str(err)))
        await page.set_content(FIXTURE)
        await page.add_style_tag(content=CSS)
        await page.add_script_tag(content=JS)
        root=page.locator('#browser-test')
        assert await root.get_attribute('data-toolbar-mode') == 'compact'
        await page.wait_for_timeout(100)
        toolbar=page.locator('.azunt-editor-toolbar')
        box=await toolbar.bounding_box()
        assert box['height'] <= 105, box
        assert await page.locator('[data-classic-groups]').is_hidden()
        assert await page.locator('[data-compact-menu]').is_visible()
        # Menus move the live controls; no replacement of the text canvas.
        original_html=await page.evaluate('AzuntEditor.getHtml("browser-test")')
        await page.locator('[data-azunt-menu="format"]').click()
        assert await page.locator('[data-menu-panel] [data-group-label="Font and color"]').is_visible()
        await page.locator('[data-menu-panel] [data-select="fontFamily"]').select_option('Georgia')
        await page.locator('[data-azunt-menu="table"]').click()
        assert await page.locator('[data-menu-panel] [data-group-label="Table"]').is_visible()
        assert await page.locator('[data-classic-groups] [data-group-label="Font and color"]').count() == 1
        await page.keyboard.press('Escape')
        assert await page.locator('[data-menu-panel]').is_hidden()
        assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == original_html
        # Normal quick action works without duplicating an original command button.
        await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Word</p>")')
        await page.evaluate('''() => {
            const node=document.querySelector('.azunt-editor-canvas p').firstChild;
            const range=document.createRange(); range.selectNodeContents(node);
            const selection=window.getSelection(); selection.removeAllRanges(); selection.addRange(range);
        }''')
        await page.locator('[data-quick-command="bold"]').click()
        assert '<strong>Word</strong>' in await page.evaluate('AzuntEditor.getHtml("browser-test")')
        # Table context toolbar.
        await page.evaluate('AzuntEditor.setHtml("browser-test", "<table><tr><td>A</td><td>B</td></tr></table>")')
        await page.locator('.azunt-editor-canvas td').first.click()
        assert await page.locator('[data-context-tools]').is_visible()
        await page.locator('[data-context-tools] [data-quick-command="table-row-after"]').click()
        assert await page.locator('.azunt-editor-canvas tr').count() == 2
        # Image context toolbar.
        await page.evaluate('AzuntEditor.setHtml("browser-test", \'<p><img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+8qV0AAAAASUVORK5CYII=" width="100"></p>\')')
        await page.locator('.azunt-editor-canvas img').click()
        assert await page.locator('[data-context-tools]').is_visible()
        await page.locator('[data-context-tools] [data-operate-menu="image"]').click()
        assert await page.locator('[data-menu-panel] [data-group-label="Image"]').is_visible()
        await page.keyboard.press('Escape')
        # Scoped custom commands are visible in Export menu and can be run.
        await page.evaluate('''() => {
          window.__ran = 0;
          AzuntEditor.registerCommand({id:'testExport',label:'Download PDF',group:'export',
            execute: (context)=>{window.__ran++; window.__snapshot=context.getHtml();}},'browser-test');
        }''')
        await page.locator('[data-azunt-menu="export"]').click()
        await page.locator('[data-menu-panel] [data-custom-command="testExport"]').click()
        assert await page.evaluate('window.__ran') == 1
        assert await page.locator('[data-menu-panel]').is_hidden()
        # Blazor-hosted commands invoke a method by command ID, not by sending full HTML.
        await page.evaluate('''() => {
          window.__blazorCalls = [];
          AzuntEditor.mount('browser-test', {
            invokeMethodAsync: (method, id) => {
              window.__blazorCalls.push([method, id]);
              return Promise.resolve();
            }
          });
          AzuntEditor.setCommands('browser-test', [
            {id:'testBlazor',label:'Blazor Excel',group:'Export',placement:'Menu'}
          ]);
        }''')
        await page.locator('[data-azunt-menu="export"]').click()
        await page.locator('[data-menu-panel] [data-custom-command="testBlazor"]').click()
        assert ['OnCustomCommand','testBlazor'] in await page.evaluate('window.__blazorCalls')
        # Switching to Classic keeps the same canvas and commands.
        await page.evaluate('AzuntEditor.setToolbarMode("browser-test","Classic")')
        assert await page.locator('[data-compact-menu]').is_hidden()
        assert await page.locator('[data-classic-groups] [data-command="bold"]').is_visible()
        assert await page.locator('[data-classic-custom] [data-custom-command="testExport"]').is_visible()
        await page.evaluate('AzuntEditor.setToolbarMode("browser-test","Compact")')
        assert await page.locator('[data-compact-menu]').is_visible()
        # Narrow hosts still show two lines and the More popup.
        await page.set_viewport_size({'width':440,'height':740})
        await page.wait_for_timeout(100)
        assert await toolbar.bounding_box() is not None
        assert (await toolbar.bounding_box())['height'] <= 105
        await page.locator('[data-azunt-menu="more"]').click()
        assert await page.locator('[data-menu-panel] [data-group-label="Document"]').is_visible()
        assert not errors, errors
        print('PASS: adaptive toolbar, menus, selection tools, custom commands, Classic mode, responsive sizing')
        await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
