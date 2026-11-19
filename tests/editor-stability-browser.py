"""Optional browser regression checks (requires Python Playwright and Chromium).
Run: python tests/editor-stability-browser.py
No Python dependency is shipped with the Azunt.Editor NuGet packages.
"""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'
HTML = ROOT / 'tests/editor-features-browser.html'

async def run():
    async with async_playwright() as playwright:
        browser_options = {'headless': True}
        chromium_path = os.environ.get('AZUNT_CHROMIUM_PATH')
        if not chromium_path and Path('/usr/bin/chromium').exists():
            chromium_path = '/usr/bin/chromium'
        if chromium_path:
            browser_options['executable_path'] = chromium_path
        if os.name != 'nt':
            browser_options['args'] = ['--no-sandbox', '--disable-dev-shm-usage']
        browser = await playwright.chromium.launch(**browser_options)
        try:
            page = await browser.new_page()
            html = HTML.read_text().replace('<script src="../src/Azunt.Editor/wwwroot/js/azunt-editor.js"></script>', '')
            await page.set_content(html)
            await page.add_style_tag(content=CSS.read_text())
            await page.add_script_tag(content=JS.read_text())
            # The compact fixture predates Undo/Redo toolbar buttons.
            await page.evaluate('''''''''() => {
                const bar = document.querySelector('.az-azunt-editor-toolbar');
                bar.insertAdjacentHTML('beforeend',
                    '<button type="button" data-command="undo">Undo</button>' +
                    '<button type="button" data-command="redo">Redo</button>');
            }''''''''' )

            async def saved():
                return await page.evaluate('AzuntEditor.getHtml("browser-test")')

            await page.evaluate('(html) => AzuntEditor.setHtml("browser-test", html)',
                                '<p>Safe</p><script>alert(1)</script><img src="javascript:bad()">')
            normalized = await saved()
            assert '<script' not in normalized and 'javascript:' not in normalized
            assert '<p>Safe</p>' in normalized

            for i in range(75):
                await page.evaluate('''i => {
                    const canvas = document.querySelector('.az-azunt-editor-canvas');
                    canvas.innerHTML = `<p>Revision ${i}</p>`;
                    canvas.dispatchEvent(new Event('input', {bubbles:true}));
                }''', i)
            assert 'Revision 74' in await saved()
            await page.locator('[data-command="undo"]').click()
            assert 'Revision 73' in await saved(), 'Undo must restore the previous revision'
            await page.locator('[data-command="redo"]').click()
            assert 'Revision 74' in await saved(), 'Redo must restore the latest revision'

            # Repeated 4MB inline image revisions are trimmed from undo history.
            base64_text = 'A' * (4 * 1024 * 1024)
            await page.evaluate('''base64 => {
                const canvas = document.querySelector('.az-azunt-editor-canvas');
                for (let i = 0; i < 12; i++) {
                    canvas.innerHTML = `<p>Big ${i}</p><img src="data:image/png;base64,${base64}">`;
                    canvas.dispatchEvent(new Event('input', {bubbles:true}));
                }
            }''', base64_text)
            assert 'Big 11' in await saved()
            await page.locator('[data-command="undo"]').click()
            assert 'Big 10' in await saved(), 'Recent undo step must remain available'
            print('PASS: HTML safety, bounded history, undo/redo for small and large documents')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(run())
