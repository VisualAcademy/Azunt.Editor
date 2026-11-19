"""Chromium regression tests for Azunt.Editor 0.5 writing tools.
Run: python tests/editor-writing-browser.py (requires Python Playwright + Chromium).
No non-Microsoft NuGet dependencies are added to the product.
"""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = ROOT / 'tests/editor-features-browser.html'
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'

async def main():
    async with async_playwright() as pw:
        opts = {'headless': True}
        if os.getenv('AZUNT_CHROMIUM_PATH'):
            opts['executable_path'] = os.getenv('AZUNT_CHROMIUM_PATH')
        elif Path('/usr/bin/chromium').exists():
            opts['executable_path'] = '/usr/bin/chromium'
        if os.name != 'nt': opts['args'] = ['--no-sandbox', '--disable-dev-shm-usage']
        browser = await pw.chromium.launch(**opts)
        page = await browser.new_page()
        try:
            html = HTML.read_text().replace('<script src="../src/Azunt.Editor/wwwroot/js/azunt-editor.js"></script>', '')
            await page.set_content(html)
            await page.add_style_tag(content=CSS.read_text())
            await page.add_script_tag(content=JS.read_text())

            async def load(html):
                await page.evaluate('(value) => AzuntEditor.setHtml("browser-test", value)', html)
            async def get_html():
                return await page.evaluate('AzuntEditor.getHtml("browser-test")')
            async def select_text(selector, start, end):
                await page.evaluate('''({selector,start,end}) => {
                    const node = document.querySelector(selector).firstChild;
                    const range = document.createRange();
                    range.setStart(node,start); range.setEnd(node,end);
                    const sel = window.getSelection(); sel.removeAllRanges();sel.addRange(range);
                    document.querySelector('.azunt-editor-canvas').dispatchEvent(new KeyboardEvent('keyup', {bubbles:true}));
                }''', dict(selector=selector, start=start, end=end))
            async def command(name):
                await page.locator(f'[data-command="{name}"]').click()

            # Word/web rich HTML: preserve allowed semantic structures, strip scripts / styles / event handlers.
            await load('<p>Before</p>')
            await page.evaluate('''() => {
                const node=document.querySelector('.azunt-editor-canvas p').firstChild;
                const r=document.createRange();r.setStart(node,6);r.collapse(true);
                const sel=window.getSelection();sel.removeAllRanges();sel.addRange(r);
                const clipboard=new DataTransfer();
                clipboard.setData('text/html', '<div class="MsoNormal"><p style="mso-style:evil;color:red" onclick="evil()"><strong>Word</strong> text</p><script>evil()</script><table><tr><td>Safe cell</td></tr></table></div>');
                clipboard.setData('text/plain', 'Word text');
                const ev=new ClipboardEvent('paste', { bubbles:true, cancelable:true, clipboardData:clipboard });
                document.querySelector('.azunt-editor-canvas').dispatchEvent(ev);
            }''')
            result = await get_html()
            assert '<strong>Word</strong>' in result and '<table>' in result and 'Safe cell' in result, result
            assert 'onclick' not in result and '<script' not in result and 'mso-style' not in result, result

            # Find / Replace / Replace all, do not flatten formatting or tables.
            await load('<p>alpha beta alpha</p><p><strong>Alpha</strong> beta</p>')
            await command('find')
            await page.locator('[data-find-query]').fill('alpha')
            await page.locator('[data-find-next]').click()
            assert (await page.evaluate('window.getSelection().toString()')).lower() == 'alpha'
            await page.locator('[data-find-replacement]').fill('gamma')
            await page.locator('[data-find-replace]').click()
            result = await get_html()
            assert 'gamma' in result and 'alpha' in result.lower(), result
            await page.locator('[data-find-all]').click()
            result = await get_html()
            assert 'alpha' not in result.lower() and 'gamma' in result, result
            assert '<strong>gamma</strong>' in result.lower(), result

            # Clear only selected formatting; preserve text.
            await load('<p><strong>Bold text</strong> after</p>')
            await select_text('.azunt-editor-canvas strong', 0, 9)
            await command('clear-format')
            result = await get_html()
            assert '<strong>Bold text</strong>' not in result and 'Bold text' in result, result

            # Special character and two distinct horizontal rules, plus HTML round-trip.
            await load('<p>Hello</p>')
            await select_text('.azunt-editor-canvas p', 5, 5)
            await command('special-chars')
            await page.locator('[data-insert-character="©"]').click()
            assert 'Hello©' in await get_html(), await get_html()
            await command('horizontal-rule')
            await command('page-break')
            result = await get_html()
            assert result.count('<hr') == 2 and 'data-azunt-page-break="true"' in result, result
            await load(result)
            assert 'data-azunt-page-break="true"' in await get_html()

            # Fullscreen toggle and escape, non-persistent metadata/counter.
            await command('fullscreen')
            assert 'azunt-editor-fullscreen' in await page.locator('.azunt-editor').get_attribute('class')
            await page.locator('.azunt-editor-canvas').press('Escape')
            assert 'azunt-editor-fullscreen' not in await page.locator('.azunt-editor').get_attribute('class')
            await load('<p>One two 세개!</p>')
            stats = await page.locator('[data-editor-stats]').inner_text()
            assert '3 words' in stats and '11 characters' in stats, stats
            assert 'words' not in await get_html()
            print('PASS: clean paste, find/replace, clear format, page break, special chars, fullscreen, word count')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
