"""Check Compact menus and group alignment on wide host pages.

Run: python tests/editor-wide-toolbar-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(
            executable_path='/usr/bin/chromium',
            args=['--no-sandbox', '--disable-dev-shm-usage'],
            headless=True)
        page = await browser.new_page(viewport={'width': 1900, 'height': 960})
        await page.set_content(HTML)
        await page.add_style_tag(content=CSS)
        await page.add_script_tag(content=JS)
        editor = page.locator('#browser-test')
        await editor.evaluate('(node) => { node.style.width = "1500px"; node.style.maxWidth="100%"; }')
        await page.locator('[data-azunt-menu="more"]').click()
        menu = page.locator('[data-menu-panel]')
        assert await menu.is_visible()
        menu_box = await menu.bounding_box()
        assert menu_box['width'] <= 866, menu_box
        assert await menu.evaluate('(el) => getComputedStyle(el).display') == 'grid'
        groups = page.locator('[data-menu-panel] .azunt-editor-group')
        assert await groups.count() >= 3
        for i in range(await groups.count()):
            group = groups.nth(i)
            # Group width may vary, but must never overflow the menu.
            bounds = await group.bounding_box()
            assert bounds['x'] >= menu_box['x'] - 1
            assert bounds['x'] + bounds['width'] <= menu_box['x'] + menu_box['width'] + 1
        toolbar = page.locator('.azunt-editor-toolbar')
        assert (await toolbar.bounding_box())['height'] <= 105
        await page.keyboard.press('Escape')
        assert await menu.is_hidden()
        await page.set_viewport_size({'width': 460, 'height': 900})
        await page.locator('[data-azunt-menu="more"]').click()
        assert await menu.is_visible()
        assert (await menu.bounding_box())['width'] <= 460
        await browser.close()
    print('PASS: wide Compact toolbar grid, balanced groups, narrow menu layout')


if __name__ == '__main__':
    asyncio.run(main())
