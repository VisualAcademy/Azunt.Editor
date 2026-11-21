"""0.8.2: viewport-safe menu positioning, keyboard and coarse-pointer polish.
Run: python tests/editor-ux-082-browser.py
No .NET host required; uses local Chromium and the real editor assets.
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')

async def mount(browser, *, width=1280, height=850, margin=0, mode='compact', touch=False, dark=False):
    context = await browser.new_context(viewport={'width': width, 'height': height},
                                        has_touch=touch, is_mobile=touch,
                                        color_scheme='dark' if dark else 'light')
    page = await context.new_page()
    errors=[]
    page.on('pageerror', lambda e: errors.append(str(e)))
    await page.set_content(HTML)
    await page.add_style_tag(content=CSS)
    # Make all test controls use the actual editor CSS classes.
    await page.evaluate('''(margin) => {
      const editor = document.querySelector('#browser-test');
      document.body.style.marginTop = margin + 'px';
      editor.style.width = 'min(960px, 100%)';
      editor.style.marginLeft = '12px';
      const more = editor.querySelector('[data-azunt-menu="more"]');
      more.classList.add('az-azunt-editor-menu-trigger', 'az-azunt-editor-quick-more');
      const group = editor.querySelector('[data-group-label="Writing tools"]');
      group.insertAdjacentHTML('beforeend', '<button class="az-azunt-editor-button" type="button" data-azunt-menu="codepicker">Code options \\u25be</button>');
    }''', margin)
    await page.add_script_tag(content=JS)
    if mode == 'classic':
        await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Classic")')
    return context, page, errors

async def box(page, selector):
    bounds = await page.locator(selector).bounding_box()
    assert bounds, selector
    return bounds

async def run():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path='/usr/bin/chromium',headless=True,
                                           args=['--no-sandbox','--disable-dev-shm-usage'])
        try:
            # Bug regression: Classic menu used absolute CSS with viewport JS coordinates.
            ctx, page, errors = await mount(browser, margin=150, mode='classic')
            trigger = page.locator('[data-azunt-menu="codepicker"]')
            await trigger.click()
            panel = page.locator('[data-menu-panel]')
            assert await panel.is_visible()
            target = await page.locator('.az-azunt-editor-toolbar').bounding_box()
            positioned = await box(page,'[data-menu-panel]')
            assert await panel.evaluate('x => getComputedStyle(x).position') == 'fixed'
            assert abs(positioned['x'] - (target['x'] + 8)) < 35, (target,positioned)
            assert abs(positioned['y'] - (target['y'] + target['height'])) < 10, (target,positioned)
            assert 'Insert code language' == await panel.get_attribute('aria-label')
            await page.keyboard.press('Escape')
            assert await panel.is_hidden()
            assert await trigger.evaluate('(el) => document.activeElement === el')
            assert not errors, errors
            await ctx.close()

            # When a large More menu is opened toward the bottom, prefer above.
            ctx, page, errors = await mount(browser, height=750, margin=390)
            more = page.locator('[data-azunt-menu="more"]')
            await more.click()
            menu = page.locator('[data-menu-panel]')
            menu_box = await box(page,'[data-menu-panel]')
            toolbar_box = await box(page,'.az-azunt-editor-toolbar')
            assert menu_box['y']+menu_box['height'] <= toolbar_box['y']+2, (menu_box,toolbar_box)
            assert menu_box['height'] > 260, menu_box
            assert menu_box['x'] >= 0 and menu_box['x'] + menu_box['width'] <= 1280
            assert 'More editor commands' == await menu.get_attribute('aria-label')
            await page.keyboard.press('Escape')
            assert await menu.is_hidden()
            await ctx.close()

            # Touch targets and scoped dark-mode colors without changing desktop heights.
            for dark in (False, True):
                ctx, page, errors = await mount(browser, width=420,height=820,touch=True,dark=dark)
                more = page.locator('[data-azunt-menu="more"]')
                height = await more.evaluate('(el) => el.getBoundingClientRect().height')
                assert height >= 40, height
                await more.click()
                button = page.locator('[data-menu-panel] button').first
                assert await button.evaluate('(el) => el.getBoundingClientRect().height') >= 40
                c = await page.locator('[data-menu-panel]').evaluate('(el) => getComputedStyle(el).backgroundColor')
                assert c == ('rgb(30, 41, 59)' if dark else 'rgb(255, 255, 255)'), (dark,c)
                await page.keyboard.press('Escape')
                assert not errors, errors
                await ctx.close()
            print('PASS: Classic viewport dropdown/keyboard, More above, touch targets, dark/light')
        finally:
            await browser.close()

if __name__ == '__main__': asyncio.run(run())
