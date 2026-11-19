"""Compact toolbar 0.6.2 regression checks (viewport overlays, formatting and touch).

Run: python tests/editor-062-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')


async def setup(browser, width, height, *, is_mobile=False, color_scheme='light'):
    context = await browser.new_context(viewport={'width': width, 'height': height},
                                        has_touch=is_mobile, is_mobile=is_mobile,
                                        color_scheme=color_scheme)
    page = await context.new_page()
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    mobile_html = HTML.replace('<head>', '<head><meta name="viewport" content="width=device-width, initial-scale=1">') if is_mobile else HTML
    await page.set_content(mobile_html)
    await page.add_style_tag(content=CSS)
    await page.evaluate("""() => {
      document.querySelectorAll('[data-compact-menu] button').forEach(el => el.classList.add('az-azunt-editor-menu-trigger'));
      document.querySelector('[data-azunt-menu="more"]').classList.add('az-azunt-editor-menu-trigger', 'az-azunt-editor-quick-more');
    }""")
    await page.add_script_tag(content=JS)
    await page.evaluate('''() => {
      const label = document.createElement('span');
      label.dataset.currentFormat = '';
      label.className = 'az-azunt-editor-current-format';
      label.textContent = 'Paragraph';
      document.querySelector('#browser-test').append(label);
      AzuntEditor.unmount('browser-test');
      AzuntEditor.mount('browser-test');
    }''')
    return context, page, errors


async def inside_viewport(page, selector):
    box = await page.locator(selector).bounding_box()
    assert box, f'{selector} not visible'
    size = page.viewport_size
    assert box['x'] >= -1 and box['x'] + box['width'] <= size['width'] + 1, (selector, box, size)
    assert box['y'] >= -1 and box['y'] + box['height'] <= size['height'] + 1, (selector, box, size)
    return box


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        for width in (280, 320, 390, 768, 2560):
            ctx, page, errors = await setup(browser, width, 800)
            await page.locator('[data-azunt-menu="more"]').click()
            await inside_viewport(page, '[data-menu-panel]')
            assert await page.locator('[data-menu-panel] [data-group-label="Font and color"]').is_visible()
            await page.keyboard.press('Escape')
            assert await page.locator('[data-menu-panel]').is_hidden()
            assert not errors, (width, errors)
            await ctx.close()

        ctx, page, errors = await setup(browser, 460, 440)
        await page.evaluate('''() => {
          document.body.style.minHeight = '2000px';
          document.querySelector('#browser-test').style.marginTop = '1000px';
          window.scrollTo(0, 640);
        }''')
        await page.locator('[data-azunt-menu="more"]').click()
        above = await inside_viewport(page, '[data-menu-panel]')
        assert above['height'] > 20, above
        await page.keyboard.press('Escape')

        # Selecting inline text formatting updates the live Font/Size/Color inputs.
        await page.evaluate('''() => {
          AzuntEditor.setHtml('browser-test', '<p><span style="font-family:Georgia;font-size:24px;color:#ff0000;background-color:#ffff00"><strong>Styled</strong></span> Plain</p>');
          const text = document.querySelector('.az-azunt-editor-canvas strong').firstChild;
          const range = document.createRange(); range.setStart(text, 2); range.collapse(true);
          const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(range);
        }''')
        await page.wait_for_timeout(60)
        assert 'Georgia' in await page.locator('[data-current-format]').inner_text()
        assert '24px' in await page.locator('[data-current-format]').inner_text()
        assert 'Text #ff0000' in await page.locator('[data-current-format]').inner_text()
        assert 'Highlight #ffff00' in await page.locator('[data-current-format]').inner_text()
        await page.locator('[data-azunt-menu="format"]').click()
        assert await page.locator('[data-menu-panel] [data-select="fontFamily"]').input_value() == 'Georgia'
        assert await page.locator('[data-menu-panel] [data-select="fontSize"]').input_value() == '24'
        assert await page.locator('[data-menu-panel] [data-select="color"]').input_value() == '#ff0000'
        assert await page.locator('[data-menu-panel] [data-select="backgroundColor"]').input_value() == '#ffff00'
        await page.keyboard.press('Escape')

        # Context tools must be placed inside the window for narrow forms.
        await page.evaluate('AzuntEditor.setHtml("browser-test", "<table><tr><td>A</td><td>B</td></tr></table>")')
        await page.locator('.az-azunt-editor-canvas td').first.click()
        await inside_viewport(page, '[data-context-tools]')
        assert await page.locator('[data-context-tools] button').count() >= 3
        await page.locator('[data-context-tools] [data-operate-menu="table"]').click()
        await inside_viewport(page, '[data-menu-panel]')
        assert await page.locator('[data-menu-panel] [data-group-label="Table"]').is_visible()
        await page.keyboard.press('Escape')
        assert not errors, errors
        await ctx.close()

        for color_scheme in ('light', 'dark'):
            ctx, page, errors = await setup(browser, 375, 760, color_scheme=color_scheme)
            await page.locator('[data-azunt-menu="more"]').click()
            color = await page.locator('[data-menu-panel]').evaluate('(el) => getComputedStyle(el).backgroundColor')
            assert color == ('rgb(30, 41, 59)' if color_scheme == 'dark' else 'rgb(255, 255, 255)'), color
            assert not errors, errors
            await ctx.close()

        ctx, page, errors = await setup(browser, 390, 820, is_mobile=True)
        await page.locator('[data-azunt-menu="more"]').tap()
        await inside_viewport(page, '[data-menu-panel]')
        height = await page.locator('[data-azunt-menu="more"]').evaluate('(el) => getComputedStyle(el).height')
        assert float(height.replace('px', '')) >= 40, height
        assert not errors, errors
        await ctx.close()
        await browser.close()
    print('PASS: 0.6.2 responsive menus, viewport bounds, font/color state, context tools, dark mode and touch')


if __name__ == '__main__':
    asyncio.run(main())
