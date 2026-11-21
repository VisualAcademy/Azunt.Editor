"""More panel editor-width alignment, toolbar heights, viewport clipping and menu regressions.
Run: python tests/editor-more-menu-layout-080-browser.py
This is a Chromium DOM regression; it does not replace a .NET host smoke test.
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT/'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS = (ROOT/'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
CS = (ROOT/'src/Azunt.Editor/Rendering/EditorMarkup.cs').read_text(encoding='utf-8')
HTML = (ROOT/'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')
assert 'az-azunt-editor-quick-more' in CS
assert 'instance.menuPanel.dataset.menuKind = key;' in JS
assert 'instance.openMenu === "more" && !contextOpener' in JS

# Mirror the CSS classes of the shared Razor/TagHelper markup in the fixture.
HTML = HTML.replace('<button data-azunt-menu="more" type="button">More</button>',
                    '<button class="az-azunt-editor-menu-trigger az-azunt-editor-quick-more" data-azunt-menu="more" type="button">More ⋯</button>')
HTML = HTML.replace('<button data-quick-command="bold" type="button">bold</button>',
                    '<button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="bold" type="button">B</button>')
HTML = HTML.replace('<button data-quick-command="italic" type="button">italic</button>',
                    '<button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="italic" type="button">I</button>')

async def bounds(locator):
    result = await locator.bounding_box()
    assert result is not None
    return result

async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                                  args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            for width, editor_width, offset, scheme in [
                (1920, 1520, 125, 'light'),
                (1380, 1080, 130, 'dark'),
                (760, 720, 12, 'light'),
                (390, 0, 0, 'light'),
            ]:
                page = await browser.new_page(viewport={'width': width,'height': 900}, color_scheme=scheme)
                errors = []
                page.on('pageerror', lambda err: errors.append(str(err)))
                await page.set_content(HTML)
                await page.add_style_tag(content=CSS)
                await page.add_script_tag(content=JS)
                if editor_width:
                    await page.locator('#browser-test').evaluate('''(el, s) => {
                        el.style.width = s.width + 'px'; el.style.marginLeft = s.margin + 'px';
                    }''', {'width':editor_width, 'margin':offset})
                root = page.locator('#browser-test')
                more = root.locator('[data-azunt-menu="more"]')
                bold = root.locator('[data-quick-command="bold"]')
                more_box, bold_box = await bounds(more), await bounds(bold)
                assert abs(more_box['height'] - bold_box['height']) <= 1, (width, more_box, bold_box)
                assert abs((more_box['y'] + more_box['height']/2) - (bold_box['y'] + bold_box['height']/2)) <= 1, (width,more_box,bold_box)
                assert abs(more_box['height'] - 34) <= 1, (width, more_box)
                await more.click()
                panel = root.locator('[data-menu-panel]')
                assert await panel.is_visible()
                assert await panel.get_attribute('data-menu-kind') == 'more'
                editor_box, panel_box = await bounds(root), await bounds(panel)
                assert abs(panel_box['x'] - max(2, editor_box['x'] + 1)) <= 2, (width, editor_box, panel_box)
                assert abs(panel_box['width'] - (editor_box['width'] - 2)) <= 3, (width, editor_box, panel_box)
                assert panel_box['x'] >= -1 and panel_box['x'] + panel_box['width'] <= width + 1
                assert await panel.evaluate('(el) => getComputedStyle(el).display') == 'grid'
                groups = panel.locator('.az-azunt-editor-group')
                assert await groups.count() >= 5
                for index in range(await groups.count()):
                    box = await bounds(groups.nth(index))
                    assert box['x'] >= panel_box['x'] - 1, (width, index, box, panel_box)
                    assert box['x'] + box['width'] <= panel_box['x'] + panel_box['width'] + 1
                if width >= 1080:
                    columns = await panel.evaluate('(el) => getComputedStyle(el).gridTemplateColumns.split(" ").length')
                    assert columns >= 4, (width,columns)
                await page.keyboard.press('Escape')
                assert await panel.is_hidden()
                assert await panel.get_attribute('data-menu-kind') is None
                await root.locator('[data-azunt-menu="format"]').click()
                format_box = await bounds(panel)
                assert format_box['width'] <= min(866, width), (width,format_box)
                assert await panel.get_attribute('data-menu-kind') == 'format'
                await page.keyboard.press('Escape')
                assert not errors, (width, errors)
                await page.close()
            # Width updates must reflow while the popover is open; fullscreen too.
            page = await browser.new_page(viewport={'width': 1340,'height': 880})
            await page.set_content(HTML)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            root=page.locator('#browser-test')
            panel=root.locator('[data-menu-panel]')
            await root.locator('[data-azunt-menu="more"]').click()
            await page.set_viewport_size({'width':900,'height':880})
            await page.wait_for_timeout(150)
            assert (await bounds(panel))['width'] <= 900
            await page.keyboard.press('Escape')
            await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Classic")')
            assert await root.locator('[data-classic-groups]').is_visible()
            await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Compact")')
            await page.evaluate('''document.querySelector('#browser-test').classList.add('az-azunt-editor-fullscreen')''')
            await root.locator('[data-azunt-menu="more"]').click()
            full_box = await bounds(panel)
            assert full_box['x'] <= 3 and full_box['width'] >= 894, full_box
            await page.keyboard.press('Escape')
            await browser.close()
            print('PASS: More 34px, edge-aligned full editor width, 4+ columns wide, mobile/dark, non-More menus, resize, fullscreen, Classic')
        finally:
            if browser.is_connected(): await browser.close()

if __name__=='__main__':
    asyncio.run(main())
