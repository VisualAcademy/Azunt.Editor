"""0.6.7 HTML checkbox/radio split toolbar and sanitizer browser regressions.
Run: python tests/editor-form-controls-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
FIXTURE = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')


def fixture():
    split = '''<span class="az-azunt-editor-form-split" role="group" aria-label="Insert HTML form control">
      <button type="button" class="az-azunt-editor-button az-azunt-editor-form-primary" data-quick-command="form-checkbox">☐</button>
      <button type="button" class="az-azunt-editor-button az-azunt-editor-form-dropdown" data-azunt-menu="controls" aria-expanded="false">▾</button></span>'''
    html = FIXTURE.replace('<span data-quick-custom=""></span>', split + '<span data-quick-custom=""></span>')
    return html


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page(viewport={'width': 1280, 'height': 850})
            errors = []
            page.on('pageerror', lambda e: errors.append(str(e)))
            await page.set_content(fixture())
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            canvas = page.locator('#browser-test .az-azunt-editor-canvas')
            assert await canvas.get_attribute('contenteditable') == 'true'
            # Main split button always inserts a default unchecked checkbox.
            await canvas.click()
            await page.locator('#browser-test [data-quick-command="form-checkbox"]').click()
            assert await canvas.locator('input[type="checkbox"]').count() == 1
            assert 'checked' not in (await page.evaluate('AzuntEditor.getHtml("browser-test")')).split('>')[1]

            # Arrow dropdown offers all four variants, not host custom commands.
            dropdown = page.locator('#browser-test [data-quick-row], #browser-test [data-compact-quick]')
            await page.locator('#browser-test [data-azunt-menu="controls"]').first.click()
            menu = page.locator('#browser-test [data-menu-panel]')
            assert await menu.is_visible()
            for key in ('form-radio', 'form-radio-checked', 'form-checkbox', 'form-checkbox-checked'):
                assert await menu.locator(f'[data-command="{key}"]').count() == 1
            assert await page.locator('#browser-test [data-azunt-menu="controls"]').first.get_attribute('aria-expanded') == 'true'
            # Arrow and keyboard menu navigation work on the split dropdown.
            await page.keyboard.press('Escape')
            await page.locator('#browser-test [data-azunt-menu="controls"]').first.focus()
            await page.keyboard.press('ArrowDown')
            assert await menu.is_visible()
            assert await page.locator('#browser-test [data-command="form-radio"]').evaluate("el => document.activeElement === el")
            await page.keyboard.press('Escape')
            assert await menu.is_hidden()
            await page.locator('#browser-test [data-azunt-menu="controls"]').first.click()
            await menu.locator('[data-command="form-radio-checked"]').click()
            assert await menu.is_hidden()
            assert await canvas.locator('input[type="radio"][checked]').count() == 1
            for command in ('form-radio', 'form-checkbox-checked'):
                await page.locator('#browser-test [data-azunt-menu="controls"]').first.click()
                await menu.locator(f'[data-command="{command}"]').click()
            assert await canvas.locator('input[type="radio"]').count() == 2
            assert await canvas.locator('input[type="checkbox"]').count() == 2
            assert await canvas.locator('input[checked]').count() == 2

            # Native click toggles state; serializing HTML must reflect LIVE checked state.
            first_checkbox = canvas.locator('input[type="checkbox"]').first
            await first_checkbox.click()
            assert await first_checkbox.is_checked()
            saved = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert saved.count('type="checkbox"') == 2
            assert saved.count('type="radio"') == 2
            assert saved.count(' checked') == 3, saved
            # Sanitize removes non-whitelisted input types and unrelated attributes.
            unsafe = '''<p><input type="checkbox" name="payments" value="yes" onclick="alert(1)" checked>ok</p>
                <input type="text" name="secret"><input type="hidden" value="123">
                <input type="radio" id="x" autofocus><script>alert(1)</script>'''
            cleaned = await page.evaluate('(html) => AzuntEditor.sanitizeHtml(html)', unsafe)
            assert cleaned.count('<input') == 2, cleaned
            assert 'type="text"' not in cleaned and 'type="hidden"' not in cleaned
            assert all(key not in cleaned for key in ('name=', 'onclick', 'value=', 'autofocus', '<script'))
            assert '<input type="checkbox" checked' in cleaned and '<input type="radio">' in cleaned

            await page.evaluate('(h) => AzuntEditor.setHtml("browser-test", h)', saved)
            assert await canvas.locator('input').count() == 4
            assert await canvas.locator('input[checked]').count() == 3
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            assert await canvas.get_attribute('contenteditable') == 'false'
            assert await canvas.locator('input:disabled').count() == 4
            assert await page.locator('#browser-test .az-azunt-editor-toolbar').is_hidden()
            assert (await page.evaluate('AzuntEditor.getHtml("browser-test")')).count('<input') == 4
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert await canvas.locator('input:disabled').count() == 0
            assert await canvas.locator('input[checked]').count() == 3
            assert saved == (await page.evaluate('AzuntEditor.getHtml("browser-test")'))

            # Existing Export command registration survives mode switching.
            await page.evaluate('''() => {
                window.exportCalls = [];
                AzuntEditor.registerCommand({id:'testExport', label:'Download PDF', group:'export',
                    execute: () => window.exportCalls.push('pdf')}, 'browser-test');
            }''')
            await page.locator('#browser-test [data-azunt-menu="export"]').click()
            await menu.locator('[data-custom-command="testExport"]').click()
            assert await page.evaluate('window.exportCalls') == ['pdf']
            assert not errors, errors
            print('PASS: form controls 4 variants, split button, HTML save/restore, View/Edit, security, Export')
        finally:
            await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
