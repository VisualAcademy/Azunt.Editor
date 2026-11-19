"""Browser regression for 0.6.5 Edit / View and dynamic mode transitions.
Run: python tests/editor-view-mode-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
FIXTURE = (ROOT / 'tests/editor-adaptive-browser.html').read_text()


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page(viewport={'width': 1200, 'height': 800})
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            html = FIXTURE.replace('data-toolbar-mode="compact"', 'data-toolbar-mode="compact" data-editor-mode="view"')
            html = html.replace('name="Html"><p>Initial</p>',
                                'name="Html" disabled><h2>Hello viewer</h2><p>Inline <strong>bold</strong> <a href="https://example.com">link</a></p><table><tr><td>Cell</td></tr></table>')
            await page.set_content(html)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            canvas = page.locator('#browser-test .az-azunt-editor-canvas')
            toolbar = page.locator('#browser-test .az-azunt-editor-toolbar')
            assert await toolbar.is_hidden(), 'View mode must hide the whole toolbar'
            assert await canvas.is_visible(), 'Viewer must show its document'
            assert await canvas.get_attribute('contenteditable') == 'false'
            assert await canvas.get_attribute('role') == 'document'
            assert await page.locator('#browser-test [data-azunt-value]').is_disabled()
            assert '<strong>bold</strong>' in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert await page.locator('#browser-test .az-azunt-editor-canvas table td').inner_text() == 'Cell'
            await canvas.click(position={'x': 80, 'y': 80})
            before = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            await page.keyboard.type('MUST NOT APPEAR')
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == before

            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert await toolbar.is_visible() and await canvas.get_attribute('contenteditable') == 'true'
            assert await canvas.get_attribute('role') == 'textbox'
            assert not await page.locator('#browser-test [data-azunt-value]').is_disabled()
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == before
            await page.evaluate('''() => {
                const canvas = document.querySelector('#browser-test .az-azunt-editor-canvas');
                canvas.innerHTML = '<p>First</p>';
                canvas.dispatchEvent(new Event('input',{bubbles:true}));
                canvas.innerHTML = '<p>Second</p>';
                canvas.dispatchEvent(new Event('input',{bubbles:true}));
            }''')
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            assert await toolbar.is_hidden()
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == '<p>Second</p>'
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert await canvas.get_attribute('contenteditable') == 'true'
            await page.locator('#browser-test [data-quick-command="undo"]').click()
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == '<p>First</p>', 'Undo history must survive mode changes'

            # Pending HTML source edits, not stale canvas content, must survive View.
            await page.evaluate('''() => document.querySelector('[data-command="source"]').click()''')
            assert await page.locator('#browser-test [data-azunt-source]').is_visible()
            await page.locator('#browser-test [data-azunt-source]').fill('<h2>Source</h2><script>alert(1)</script><p>Safe</p>')
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            source_html = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert '<script>' not in source_html and 'Source' in source_html
            assert await canvas.get_attribute('contenteditable') == 'false'
            assert await page.locator('#browser-test [data-azunt-source]').is_hidden()

            # Programmatic HTML replacement is safe and keeps View non-editable.
            await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Updated</p><img src=javascript:alert(1) onerror=alert(2)>")')
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == '<p>Updated</p>'
            assert await canvas.get_attribute('contenteditable') == 'false'
            # A second editor remains editable while the first is a viewer.
            await page.evaluate('''() => {
                const original = document.getElementById('browser-test');
                const another = original.cloneNode(true);
                another.id = 'second-editor';
                another.dataset.editorMode = 'edit';
                another.querySelector('.az-azunt-editor-canvas').innerHTML = '';
                another.querySelector('[data-azunt-value]').value = '<p>Second instance</p>';
                original.after(another);
                AzuntEditor.mount('second-editor');
            }''')
            assert await page.locator('#second-editor .az-azunt-editor-toolbar').is_visible()
            assert await page.locator('#second-editor .az-azunt-editor-canvas').get_attribute('contenteditable') == 'true'
            assert await toolbar.is_hidden()
            assert 'Second instance' in await page.evaluate('AzuntEditor.getHtml("second-editor")')
            assert '<p>Updated</p>' == await page.evaluate('AzuntEditor.getHtml("browser-test")')
            try:
                await page.evaluate('AzuntEditor.setMode("browser-test", "Preview")')
                assert False, 'Unknown mode must not be accepted'
            except Exception as error:
                assert 'Editor mode must be Edit or View' in str(error)
            assert not errors, errors
            print('PASS: initial viewer, HTML sanitizer, no editing, mode switch, undo, source mode, multi-instance')
        finally:
            await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
