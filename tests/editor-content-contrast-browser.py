"""Verify editor document text is isolated from a host site's global text colors.

Run: python tests/editor-content-contrast-browser.py
Requires Playwright for Python and Chromium; no .NET host is needed.
"""
import asyncio
from pathlib import Path

from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = (ROOT / "tests/editor-adaptive-browser.html").read_text(encoding="utf-8")
CSS = (ROOT / "src/Azunt.Editor/wwwroot/css/azunt-editor.css").read_text(encoding="utf-8")
JS = (ROOT / "src/Azunt.Editor/wwwroot/js/azunt-editor.js").read_text(encoding="utf-8")
SITE_CSS = (ROOT / "samples/Azunt.Web/wwwroot/css/site.css").read_text(encoding="utf-8")


async def check(scheme, host_styles_after, browser):
    context = await browser.new_context(color_scheme=scheme)
    try:
        page = await context.new_page()
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        await page.set_content(HTML)
        if not host_styles_after:
            await page.add_style_tag(content=SITE_CSS)
        await page.add_style_tag(content=CSS)
        if host_styles_after:
            await page.add_style_tag(content=SITE_CSS)
        await page.add_script_tag(content=JS)
        await page.evaluate('AzuntEditor.mount("browser-test")')
        markup = '''<p>Plain paragraph</p><h2>Heading</h2>
        <ul><li>List item</li></ul><blockquote>Quotation</blockquote>
        <p><span style="color: #e11d48">Custom red</span> plain continuation
          <a href="https://example.com">Link</a></p>
        <p style="color: #22c55e">Custom green paragraph</p>'''
        await page.evaluate('(html) => AzuntEditor.setHtml("browser-test", html)', markup)
        # The actual site.css sets p { color: #516076 }. That value must not
        # win over the document theme in either OS mode or CSS load order.
        default = 'rgb(241, 245, 249)' if scheme == 'dark' else 'rgb(30, 41, 59)'
        for selector in ('p:first-child', 'h2', 'li', 'blockquote'):
            actual = await page.locator('.az-azunt-editor-canvas ' + selector).first.evaluate(
                'el => getComputedStyle(el).color')
            assert actual == default, (scheme, host_styles_after, selector, actual, default)
        for selector, expected in [
            ('p span[style]', 'rgb(225, 29, 72)'),
            ('p[style]', 'rgb(34, 197, 94)'),
            ('p a', 'rgb(147, 197, 253)' if scheme == 'dark' else 'rgb(29, 78, 216)')
        ]:
            actual = await page.locator('.az-azunt-editor-canvas ' + selector).first.evaluate(
                'el => getComputedStyle(el).color')
            assert actual == expected, (scheme, host_styles_after, selector, actual, expected)
        # CSS contrast should not add spurious inline color attributes to saved HTML.
        saved = await page.evaluate('AzuntEditor.getHtml("browser-test")')
        assert 'style="color: rgb(241, 245, 249)"' not in saved, saved
        assert 'rgb(34, 197, 94)' in saved and 'rgb(225, 29, 72)' in saved, saved
        assert not errors, errors
    finally:
        await context.close()


async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(
            executable_path='/usr/bin/chromium', headless=True,
            args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            for scheme in ('light', 'dark'):
                for host_styles_after in (False, True):
                    await check(scheme, host_styles_after, browser)
        finally:
            await browser.close()
    print('PASS: light/dark default document text contrast under real host CSS')
    print('PASS: host CSS before/after editor, custom inline colors, links, saved HTML')


if __name__ == '__main__':
    asyncio.run(main())
