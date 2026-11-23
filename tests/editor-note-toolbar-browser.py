"""Smoke-check the one-row Notes layout and runtime toggling.
Run: python tests/editor-note-toolbar-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        page = await browser.new_page(viewport={'width': 1120, 'height': 800})
        errors = []
        page.on('pageerror', lambda e: errors.append(str(e)))
        await page.set_content((ROOT / 'tests/editor-adaptive-browser.html').read_text())
        await page.add_style_tag(content=(ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text())
        await page.add_script_tag(content=(ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text())
        menu = page.locator('[data-compact-menu]')
        quick = page.locator('[data-compact-quick]')
        assert await menu.is_visible()
        html = await page.evaluate('AzuntEditor.getHtml("browser-test")')
        await page.evaluate('AzuntEditor.setShowMenuBar("browser-test", false)')
        assert await menu.is_hidden()
        assert await quick.is_visible()
        assert await page.locator('[data-quick-command="bold"]').is_visible()
        assert await page.locator('[data-azunt-menu="more"]').is_visible()
        assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == html
        # More remains fully functional when top menus are hidden.
        await page.locator('[data-azunt-menu="more"]').click()
        assert await page.locator('[data-menu-panel] [data-group-label="Paragraph"]').is_visible()
        await page.evaluate('AzuntEditor.setShowMenuBar("browser-test", true)')
        assert await menu.is_visible()
        assert await page.locator('[data-menu-panel]').is_hidden()
        await page.evaluate('AzuntEditor.setShowMenuBar("browser-test", false)')
        await page.locator('.az-azunt-editor-canvas').click()
        await page.keyboard.press('Alt+F10')
        assert await page.locator('[data-azunt-menu="more"]').evaluate('(e) => document.activeElement === e')
        await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Classic")')
        assert await page.locator('[data-classic-groups] [data-command="bold"]').is_visible()
        await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Compact")')
        assert await menu.is_hidden() and await quick.is_visible()
        assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == html
        assert not errors, errors
        await browser.close()
    print('Note-toolbar browser checks passed')

if __name__ == '__main__':
    asyncio.run(main())
