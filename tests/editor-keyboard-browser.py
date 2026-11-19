"""Chromium checks for keyboard menus and current-format indicators.
Run: python tests/editor-keyboard-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
FIXTURE = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        page = await browser.new_page(viewport={'width': 1200, 'height': 900})
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.set_default_timeout(4000)
        await page.set_content(FIXTURE)
        await page.evaluate("""() => {
            document.querySelector('.azunt-editor').insertAdjacentHTML('beforeend', '<span data-current-format>Paragraph</span>');
            document.querySelector('[data-group-label=\"Text formatting\"]').insertAdjacentHTML('beforeend', '<button type=\"button\" data-command=\"italic\">Italic</button>');
        }""")
        await page.add_style_tag(content=CSS)
        await page.add_script_tag(content=JS)

        async def focus_cursor(html, selector):
            await page.evaluate('([html]) => AzuntEditor.setHtml("browser-test", html)', [html])
            await page.evaluate('''(css) => {
                const element = document.querySelector('.azunt-editor-canvas ' + css);
                const text = element.firstChild;
                const range = document.createRange();
                range.setStart(text, Math.min(1, text.length));
                range.collapse(true);
                const selection = window.getSelection();
                selection.removeAllRanges(); selection.addRange(range);
                document.querySelector('.azunt-editor-canvas').focus();
            }''', selector)
            await page.wait_for_timeout(50)

        # Per-caret styles and the matching classic + quick toolbar commands.
        await focus_cursor('<h2><strong>Bold title</strong></h2><p>Regular text</p>', 'h2 strong')
        assert await page.locator('[data-quick-command="bold"]').get_attribute('aria-pressed') == 'true'
        assert await page.locator('[data-command="bold"]').get_attribute('aria-pressed') == 'true'
        assert await page.locator('[data-current-format]').inner_text() == 'Heading 2 · Bold'
        await focus_cursor('<p><em>Italic word</em> then regular</p>', 'p em')
        assert await page.locator('[data-quick-command="bold"]').get_attribute('aria-pressed') == 'false'
        assert await page.locator('[data-command="italic"]').get_attribute('aria-pressed') == 'true'
        assert await page.locator('[data-current-format]').inner_text() == 'Paragraph · Italic'
        await focus_cursor('<p>Ordinary text</p>', 'p')
        assert await page.locator('[data-quick-command="bold"]').get_attribute('aria-pressed') == 'false'
        assert await page.locator('[data-current-format]').inner_text() == 'Paragraph'

        # Alt+F10 focuses the menubar. Arrow navigation is roving and never edits HTML.
        before = await page.evaluate('AzuntEditor.getHtml("browser-test")')
        await page.locator('.azunt-editor-canvas').focus()
        await page.keyboard.press('Alt+F10')
        assert await page.evaluate('document.activeElement.dataset.azuntMenu') == 'file'
        assert await page.locator('[data-azunt-menu="file"]').get_attribute('role') == 'menuitem'
        assert await page.locator('[data-azunt-menu="file"]').get_attribute('tabindex') == '0'
        await page.keyboard.press('ArrowRight')
        assert await page.evaluate('document.activeElement.dataset.azuntMenu') == 'edit'
        assert await page.locator('[data-azunt-menu="file"]').get_attribute('tabindex') == '-1'
        await page.keyboard.press('End')
        assert await page.evaluate('document.activeElement.dataset.azuntMenu') == 'export'
        await page.keyboard.press('Home')
        assert await page.evaluate('document.activeElement.dataset.azuntMenu') == 'file'
        await page.keyboard.press('ArrowDown')
        assert await page.locator('[data-menu-panel]').is_visible()
        assert await page.evaluate('document.activeElement.closest("[data-menu-panel]") !== null')
        await page.keyboard.press('ArrowDown')
        assert await page.evaluate('document.activeElement.closest("[data-menu-panel]") !== null')
        await page.keyboard.press('Escape')
        assert await page.locator('[data-menu-panel]').is_hidden()
        assert await page.evaluate('document.activeElement.dataset.azuntMenu') == 'file'
        assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == before

        # Open via Enter, navigate menu buttons, close via Escape, and restore focus.
        await page.keyboard.press('ArrowRight')  # Edit
        await page.keyboard.press('Enter')
        assert await page.locator('[data-menu-panel]').is_visible()
        await page.keyboard.press('ArrowDown')
        assert await page.evaluate('document.activeElement.closest("[data-menu-panel]") !== null')
        await page.keyboard.press('Escape')
        assert await page.locator('[data-menu-panel]').is_hidden()
        assert await page.evaluate('document.activeElement.dataset.azuntMenu') == 'edit'

        # Native select gets arrow keys: menus do not hijack them.
        await page.keyboard.press('ArrowRight')  # View
        await page.keyboard.press('ArrowRight')  # Insert
        await page.keyboard.press('ArrowRight')  # Format
        await page.keyboard.press('ArrowDown')
        select = page.locator('[data-menu-panel] [data-select="fontFamily"]')
        await select.focus()
        await page.keyboard.press('ArrowDown')
        assert await select.is_visible()
        assert await page.locator('[data-menu-panel]').is_visible()
        await page.keyboard.press('Escape')
        assert await page.locator('[data-menu-panel]').is_hidden()

        # Moving focus out of the toolbar closes menus, with no stray popup.
        await page.locator('[data-azunt-menu="format"]').click()
        assert await page.locator('[data-menu-panel]').is_visible()
        await page.locator('.azunt-editor-canvas').focus()
        await page.wait_for_timeout(40)
        assert await page.locator('[data-menu-panel]').is_hidden()

        # A keyboard-only formatting command acts on the preserved text selection.
        await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Keyboard text</p>")')
        await page.evaluate('''() => {
          const node = document.querySelector('#browser-test .azunt-editor-canvas p').firstChild;
          const range = document.createRange(); range.setStart(node, 0); range.setEnd(node, 8);
          const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(range);
          document.querySelector('#browser-test .azunt-editor-canvas').focus();
        }''')
        await page.wait_for_timeout(40)
        await page.keyboard.press('Alt+F10')
        for _ in range(4):  # File -> Edit -> View -> Insert -> Format
            await page.keyboard.press('ArrowRight')
        await page.keyboard.press('ArrowDown')
        first = page.locator('#browser-test [data-menu-panel] button[data-command="bold"]')
        assert await first.evaluate("element => document.activeElement === element")
        await page.keyboard.press('Enter')
        assert '<strong>Keyboard</strong>' in await page.evaluate('AzuntEditor.getHtml("browser-test")')
        assert await page.locator('#browser-test [data-menu-panel]').is_hidden()

        # Multiple instances must not share menu focus/state.
        await page.evaluate('''() => {
          const cloned = document.querySelector('#browser-test').cloneNode(true);
          cloned.id = 'second-editor';
          cloned.querySelector('[data-azunt-value]').value = '<p>Other editor</p>';
          document.body.appendChild(cloned);
          AzuntEditor.mount('second-editor');
        }''')
        await page.locator('#second-editor [data-azunt-menu="table"]').click()
        assert await page.locator('#second-editor [data-menu-panel]').is_visible()
        assert await page.locator('#browser-test [data-menu-panel]').is_hidden()
        await page.locator('#second-editor [data-azunt-menu="table"]').click()
        await page.keyboard.press('Escape')
        assert not errors, errors
        print('PASS: keyboard navigation, focus restoration, format state, multi-instance isolation')
        await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
