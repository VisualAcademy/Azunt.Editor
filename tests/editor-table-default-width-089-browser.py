"""Full-width insertion across rendering modes; old auto-width tables are unchanged."""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]

async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path='/usr/bin/chromium', headless=True, args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page(viewport={'width':1100,'height':900})
            errors = []
            page.on('pageerror', lambda exc: errors.append(str(exc)))
            await page.set_content((ROOT/'tests/editor-adaptive-browser.html').read_text())
            await page.add_style_tag(content=(ROOT/'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text())
            await page.add_script_tag(content=(ROOT/'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text())
            root = page.locator('#browser-test')
            canvas = root.locator('.az-azunt-editor-canvas')
            for mode in ['Standard','BrowserDefault','CompactReport','BusinessDocument','Minimal']:
                await page.evaluate('(mode) => AzuntEditor.setContentRendering("browser-test",mode)',mode)
                await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Before</p>")')
                await canvas.locator('p').click()
                await root.locator('[data-compact-quick] [data-quick-command="table"]').click()
                table=canvas.locator('table')
                assert await table.count()==1
                async def check_width():
                    sizes = await table.evaluate('''t => {const c=t.closest('.az-azunt-editor-canvas'), s=getComputedStyle(c); return [t.getBoundingClientRect().width, c.clientWidth-parseFloat(s.paddingLeft)-parseFloat(s.paddingRight)]}''')
                    assert abs(sizes[0]-sizes[1]) < 3, (mode,sizes)
                await check_width()
                html=await page.evaluate('AzuntEditor.getHtml("browser-test")')
                assert 'width: 100%' in html, (mode,html)
                await page.evaluate('AzuntEditor.setMode("browser-test","View")')
                await check_width()
                await page.evaluate('AzuntEditor.setMode("browser-test","Edit")')
                await page.evaluate('(html) => AzuntEditor.setHtml("browser-test",html)',html)
                await check_width()
                print('PASS',mode,'insertion, serialization, Edit/View and reload')
            await page.evaluate('AzuntEditor.setContentRendering("browser-test","CompactReport")')
            await page.evaluate('AzuntEditor.setHtml("browser-test","<table><tr><td>A</td><td>B</td></tr></table>")')
            assert await canvas.locator('table').evaluate('t=>t.style.width')==''
            assert await canvas.locator('table').evaluate('t=>t.getBoundingClientRect().width') < 100
            assert 'width:' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert 'width:' not in await page.evaluate('AzuntEditor.sanitizeHtml("<table style=\'width:999%;position:fixed\'><tr><td>X</td></tr></table>")')
            assert not errors, errors
            print('PASS existing auto-width tables and rejection of unsafe widths')
        finally:
            await browser.close()

if __name__=='__main__':
    asyncio.run(main())
