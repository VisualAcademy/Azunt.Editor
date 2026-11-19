"""Optional real Chromium integration checks; requires Microsoft Playwright for Python.
No package dependency is added to Azunt.Editor itself. Run: python tests/run-browser-features.py
"""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'
HTML = ROOT / 'tests/editor-features-browser.html'

async def main():
    async with async_playwright() as playwright:
        chrome = os.environ.get('AZUNT_CHROMIUM_PATH')
        if chrome is None and Path('/usr/bin/chromium').exists():
            chrome = '/usr/bin/chromium'
        kwargs = {'headless': True}
        if chrome:
            kwargs['executable_path'] = chrome
        if os.name != 'nt':
            kwargs['args'] = ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
        browser = await playwright.chromium.launch(**kwargs)
        page = await browser.new_page()
        html = HTML.read_text().replace('<script src="../src/Azunt.Editor/wwwroot/js/azunt-editor.js"></script>', '')
        await page.set_content(html)
        await page.add_style_tag(content=CSS.read_text())
        await page.add_script_tag(content=JS.read_text())

        async def load(html):
            await page.evaluate('(v) => AzuntEditor.setHtml("browser-test", v)', html)

        async def get_html():
            return await page.evaluate('AzuntEditor.getHtml("browser-test")')

        async def select_text(selector, start=0, end=None):
            await page.evaluate('''({selector,start,end}) => {
                const el = document.querySelector(selector);
                const node = el.firstChild;
                const range = document.createRange();
                range.setStart(node, start);
                range.setEnd(node, end === null ? node.textContent.length : end);
                const selection = window.getSelection();
                selection.removeAllRanges(); selection.addRange(range);
                el.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true }));
            }''', dict(selector=selector, start=start, end=end))

        async def button(command):
            await page.locator(f'[data-command="{command}"]').click()

        assert '<p>Initial</p>' in await get_html()
        # Inline formatting and CSS/HTML round-trip.
        for command, tag in [('strike','s'), ('sup','sup'), ('sub','sub')]:
            await load('<p>Format me</p>')
            await select_text('.azunt-editor-canvas p', 0, 6)
            await button(command)
            assert f'<{tag}>Format</{tag}>' in await get_html(), (command, await get_html())
        for select, value, css in [('fontFamily','Georgia','font-family: Georgia'),
                                   ('fontSize','24','font-size: 24px')]:
            await load('<p>Format me</p>')
            await select_text('.azunt-editor-canvas p',0,6)
            await page.locator(f'[data-select="{select}"]').select_option(value)
            assert css in await get_html(), (select, await get_html())
        await load('<p>Colors</p>')
        await select_text('.azunt-editor-canvas p')
        await page.locator('[data-select="color"]').fill('#123456')
        assert 'color: rgb(18, 52, 86)' in await get_html() or 'color: #123456' in await get_html(), await get_html()
        await load('<p>Highlight</p>')
        await select_text('.azunt-editor-canvas p')
        await page.locator('[data-select="backgroundColor"]').fill('#ffff00')
        assert 'background-color:' in await get_html(), await get_html()
        # Paragraph indentation must persist through sanitizer.
        await load('<p>Indented</p>')
        await select_text('.azunt-editor-canvas p',0,0)
        await button('indent')
        assert 'margin-left: 24px' in await get_html(), await get_html()
        await button('outdent')
        assert 'margin-left:' not in await get_html(), await get_html()
        # Hyperlinks: create, edit, and unlink.
        await load('<p>Azunt Editor</p>')
        await select_text('.azunt-editor-canvas p',0,5)
        await button('link')
        await page.locator('[data-link-url]').fill('https://example.org')
        await page.locator('[data-link-apply]').click()
        assert 'href="https://example.org"' in await get_html(), await get_html()
        await select_text('.azunt-editor-canvas a')
        await button('link')
        await page.locator('[data-link-url]').fill('https://example.com')
        await page.locator('[data-link-apply]').click()
        assert 'https://example.com' in await get_html(), await get_html()
        await select_text('.azunt-editor-canvas a')
        await button('unlink')
        assert '<a ' not in await get_html(), await get_html()
        # Table row and column modifications are anchored to clicked cell.
        await load('<p>Before table</p>')
        await select_text('.azunt-editor-canvas p',0,0)
        await button('table')
        assert await page.locator('.azunt-editor-canvas td').count() == 4
        await page.locator('.azunt-editor-canvas td').first.click()
        await button('table-row-after')
        assert await page.locator('.azunt-editor-canvas tr').count() == 3
        await button('table-col-after')
        assert await page.locator('.azunt-editor-canvas td').count() == 9
        await button('table-col-delete')
        assert await page.locator('.azunt-editor-canvas td').count() == 6
        await page.locator('.azunt-editor-canvas td').first.click()
        await button('table-row-delete')
        assert await page.locator('.azunt-editor-canvas tr').count() == 2
        # Image selection, resize, alignment and deletion.
        tiny = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+8qV0AAAAASUVORK5CYII='
        await load(f'<p><img src="{tiny}" width="300" alt="tiny"></p>')
        await page.locator('.azunt-editor-canvas img').click()
        await page.locator('[data-image-width]').fill('180')
        await page.locator('[data-image-width]').dispatch_event('change')
        assert 'width="180"' in await get_html(), await get_html()
        await page.locator('[data-select="imageAlign"]').select_option('center')
        assert 'text-align: center' in await get_html(), await get_html()
        await button('image-delete')
        assert '<img' not in await get_html()
        # HTML source is editable and strips unsafe active content.
        await load('<p>Before</p>')
        await button('source')
        assert await page.locator('[data-azunt-source]').is_visible()
        await page.locator('[data-azunt-source]').fill('<p style="color:#123456">Safe</p><script>alert(1)</script><iframe src="https://evil.example"></iframe>')
        await button('source')
        safe = await get_html()
        assert '<script' not in safe and '<iframe' not in safe and 'Safe' in safe, safe
        assert 'color:' in safe, safe
        # Source mode explicit save still returns sanitized HTML.
        await button('source')
        await page.locator('[data-azunt-source]').fill('<p>Direct save</p><script>nope()</script>')
        assert '<p>Direct save</p>' in await get_html()
        await button('source')
        # Page settings are configurable for the preview.
        await page.locator('[data-select="paper"]').select_option('Letter')
        await page.locator('[data-select="orientation"]').select_option('landscape')
        await page.locator('[data-select="margin"]').select_option('narrow')
        async with page.expect_popup() as popup:
            await button('preview')
        preview = await popup.value
        await preview.wait_for_load_state('domcontentloaded')
        assert 'Letter · landscape · narrow' in await preview.locator('#azunt-print-controls').inner_text(), await preview.content()
        await preview.close()
        print('PASS: formatting, styles, links, tables, images, source mode, print preview settings')
        await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
