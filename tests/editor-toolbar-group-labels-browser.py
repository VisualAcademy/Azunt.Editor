"""Check labeled/unlabeled toolbar geometry and live toggling in Chromium."""
import asyncio
import runpy
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
fixture = runpy.run_path(str(ROOT / 'tests/editor-fullscreen-080-browser.py'))

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', args=['--no-sandbox'])
        for width, scheme, touch in [(1180, 'light', False), (1180, 'dark', False), (390, 'dark', False), (390, 'light', True), (280, 'light', False)]:
            page = await browser.new_page(viewport={'width': width, 'height': 800}, color_scheme=scheme, has_touch=touch)
            errors = []
            page.on('pageerror', lambda e: errors.append(str(e)))
            await page.set_content(fixture['HTML'])
            await page.add_style_tag(content=fixture['CSS'])
            await page.add_script_tag(content=fixture['JS'])
            root = page.locator('#browser-test')
            more = root.locator('[data-azunt-menu="more"]')
            bold = root.locator('[data-quick-command="bold"]')
            full = root.locator('[data-quick-command="fullscreen"]')
            quick = root.locator('[data-compact-quick]')
            label = root.locator('.az-azunt-editor-quick-label').nth(1)
            html = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            async def alignment():
                a, b, f, q = [await x.bounding_box() for x in (more, bold, full, quick)]
                assert abs(a['y']+a['height']/2-b['y']-b['height']/2) <= 1, (width, a, b)
                assert abs(a['height']-b['height']) <= 1, (width, a, b)
                assert a['y']+a['height'] <= q['y']+q['height']+1
                assert f['x']+f['width'] <= width+1
                return q['height']
            assert await label.is_visible()
            labeled = await alignment()
            await page.evaluate('AzuntEditor.setShowMenuBar("browser-test", false)')
            await page.evaluate('AzuntEditor.setShowToolbarGroupLabels("browser-test", false)')
            assert await label.is_hidden()
            assert await root.locator('[data-compact-menu]').is_hidden()
            assert await alignment() < labeled
            await more.click()
            group = root.locator('[data-menu-panel] [data-group-label="Paragraph"]')
            assert await group.is_visible()
            assert await group.evaluate('(e)=>getComputedStyle(e,"::before").content') == '"Paragraph"'
            await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Classic")')
            group = root.locator('[data-classic-groups] [data-group-label="Paragraph"]')
            assert await group.is_visible()
            assert await group.evaluate('(e)=>getComputedStyle(e,"::before").content') == 'none'
            await page.evaluate('AzuntEditor.setShowToolbarGroupLabels("browser-test", true)')
            assert await group.evaluate('(e)=>getComputedStyle(e,"::before").content') == '"Paragraph"'
            await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Compact")')
            assert await label.is_visible()
            await alignment()
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == html
            assert not errors, errors
            await page.close()
        await browser.close()
    print('Toolbar group labels browser checks passed')

if __name__ == '__main__':
    asyncio.run(main())
