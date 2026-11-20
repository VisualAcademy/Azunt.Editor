"""0.6.8: toolbar-anchored table size picker browser regressions.
Run: python tests/editor-table-picker-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')


def fixture():
    # Use the same split-control DOM contract as EditorMarkup.Render for both modes.
    def split(compact):
        return f'''<span class="az-azunt-editor-table-split{' az-azunt-editor-quick-optional' if compact else ''}" role="group" aria-label="Insert table">
            <button type="button" class="az-azunt-editor-button az-azunt-editor-table-primary{' az-azunt-editor-quick-command' if compact else ''}" data-{'quick-' if compact else ''}command="table">▦</button>
            <button type="button" class="az-azunt-editor-button az-azunt-editor-table-dropdown" data-azunt-menu="tablepicker" aria-expanded="false" aria-haspopup="dialog">▾</button>
            </span>'''
    return (HTML
            .replace('<button data-quick-command="table" type="button">table</button>', split(True))
            .replace('<button data-command="table" type="button">Table</button>', split(False)))


async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                           args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            for width, scheme in [(1280, 'light'), (390, 'light'), (320, 'dark')]:
                page = await browser.new_page(viewport={'width': width, 'height': 800}, color_scheme=scheme)
                errors = []
                page.on('pageerror', lambda error: errors.append(str(error)))
                await page.set_content(fixture())
                await page.add_style_tag(content=CSS)
                await page.add_script_tag(content=JS)
                editor = page.locator('#browser-test')
                menu = editor.locator('[data-menu-panel]')
                opener = editor.locator('[data-compact-quick] [data-azunt-menu="tablepicker"]')
                canvas = editor.locator('.az-azunt-editor-canvas')
                await page.evaluate('AzuntEditor.setHtml("browser-test", "")')
                await opener.click()
                assert await menu.is_visible(), 'Table dropdown opens'
                assert await menu.locator('[data-table-rows][data-table-columns]').count() == 100
                rect = await menu.bounding_box()
                trigger = await opener.bounding_box()
                assert rect['x'] >= 0 and rect['x'] + rect['width'] <= width + 1, (width, rect)
                assert rect['y'] >= trigger['y'], (rect, trigger)
                assert rect['y'] <= trigger['y'] + trigger['height'] + 12, (rect, trigger)
                await menu.locator('[data-table-rows="3"][data-table-columns="4"]').hover()
                assert await menu.locator('[data-table-size]').inner_text() == '4 × 3'
                assert await menu.locator('.az-azunt-editor-table-cell-selected').count() == 12
                await menu.locator('[data-table-rows="3"][data-table-columns="4"]').click()
                assert await menu.is_hidden(), 'Palette closes after insertion'
                assert await canvas.locator('table tr').count() == 3
                assert await canvas.locator('table tr:first-child td').count() == 4
                assert (await page.evaluate('AzuntEditor.getHtml("browser-test")')).count('<td>') == 12
                # Existing single-click shortcut continues inserting 2 x 2 tables.
                await page.evaluate('AzuntEditor.setHtml("browser-test", "")')
                await editor.locator('[data-compact-quick] [data-quick-command="table"]').click()
                assert await canvas.locator('table tr').count() == 2
                assert await canvas.locator('table td').count() == 4
                # Keyboard: ArrowDown opens palette; right / down moves selection; Enter inserts.
                await page.evaluate('AzuntEditor.setHtml("browser-test", "")')
                await opener.focus()
                await page.keyboard.press('ArrowDown')
                assert await menu.is_visible()
                assert await menu.locator('[data-table-rows="1"][data-table-columns="1"]').evaluate('(el) => document.activeElement === el')
                await page.keyboard.press('ArrowRight')
                await page.keyboard.press('ArrowDown')
                assert await menu.locator('[data-table-size]').inner_text() == '2 × 2'
                await page.keyboard.press('Enter')
                assert await canvas.locator('table tr').count() == 2
                assert await canvas.locator('table td').count() == 4
                # Escape returns focus to dropdown, without adding content.
                await opener.click()
                await page.keyboard.press('Escape')
                assert await menu.is_hidden()
                assert await opener.evaluate('(el) => document.activeElement === el')
                # Classic toolbar gets its own anchored dropdown, same 10x10 grid.
                await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Classic")')
                await page.evaluate('AzuntEditor.setHtml("browser-test", "")')
                classic = editor.locator('[data-classic-groups] [data-azunt-menu="tablepicker"]')
                await classic.click()
                assert await menu.is_visible()
                classic_rect = await menu.bounding_box()
                classic_btn = await classic.bounding_box()
                assert abs(classic_rect['y'] - classic_btn['y'] - classic_btn['height']) <= 15, (classic_rect, classic_btn)
                await menu.locator('[data-table-rows="1"][data-table-columns="10"]').click()
                assert await canvas.locator('table tr').count() == 1
                assert await canvas.locator('table td').count() == 10
                assert not errors, (width, scheme, errors)
                await page.close()
            print('PASS: 0.6.8 table grid, button anchor, 1-10 size, keyboard, classic, light/dark, narrow viewports')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
