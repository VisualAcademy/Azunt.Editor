"""0.7.0 Undo/Redo: caret restoration, IME, native history, redo branching, forms and mode changes.
Run: python tests/editor-undo-070-browser.py (requires Playwright and Chromium).
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = ROOT / 'tests/editor-adaptive-browser.html'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'

async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
            args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page()
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            await page.set_content(HTML.read_text())
            await page.add_style_tag(content=CSS.read_text())
            await page.add_script_tag(content=JS.read_text())
            canvas = page.locator('#browser-test .az-azunt-editor-canvas')

            async def saved():
                return await page.evaluate('AzuntEditor.getHtml("browser-test")')

            async def reset(html, start=None, end=None):
                await page.evaluate('''([html, start, end]) => {
                  AzuntEditor.setHtml('browser-test', html);
                  const canvas = document.querySelector('#browser-test .az-azunt-editor-canvas');
                  const node = canvas.querySelector('p')?.firstChild;
                  if (node?.nodeType === Node.TEXT_NODE) {
                    const range = document.createRange();
                    const offset = start == null ? node.length : start;
                    range.setStart(node, offset);
                    range.setEnd(node, end == null ? offset : end);
                    const selection = window.getSelection();
                    selection.removeAllRanges(); selection.addRange(range);
                  }
                  canvas.focus();
                }''', [html, start, end])

            def assert_text_contains(value, text):
                assert text in value, value

            # Real keyboard input and Ctrl+Z/Ctrl+Y restore both document and caret.
            await reset('<p>Alpha</p>')
            await page.keyboard.type('Z')
            assert await saved() == '<p>AlphaZ</p>'
            await page.keyboard.press('Control+z')
            assert await saved() == '<p>Alpha</p>'
            caret = await page.evaluate('''() => {
              const s=window.getSelection();
              return {offset:s.anchorOffset, text:s.anchorNode?.textContent,
                      inside:document.querySelector('.az-azunt-editor-canvas').contains(s.anchorNode)};
            }''')
            assert caret == {'offset': 5, 'text': 'Alpha', 'inside': True}, caret
            await page.keyboard.press('Control+y')
            assert await saved() == '<p>AlphaZ</p>'
            await page.keyboard.type('!')
            assert await saved() == '<p>AlphaZ!</p>'
            print('PASS: Ctrl+Z/Y, document snapshots, and restored caret')

            # Browser native Undo (context menu/beforeinput) must use the same history.
            await page.evaluate('''() => {
              const canvas = document.querySelector('.az-azunt-editor-canvas');
              canvas.dispatchEvent(new InputEvent('beforeinput', {
                bubbles:true, cancelable:true, inputType:'historyUndo'
              }));
            }''')
            assert await saved() == '<p>AlphaZ</p>'
            print('PASS: native beforeinput historyUndo routing')

            # Redo branch discarded following a different edit after Undo.
            await page.keyboard.type('N')
            current = await saved()
            assert_text_contains(current, 'AlphaZN')
            await page.keyboard.press('Control+y')
            assert await saved() == current
            print('PASS: new edits invalidate the previous Redo branch')

            # Select-range restoration: toolbar formatting is one atomic step.
            await reset('<p>Hello World</p>', 6, 11)
            await page.locator('#browser-test [data-quick-command="bold"]').click()
            assert '<strong>World</strong>' in await saved()
            await page.locator('#browser-test [data-quick-command="undo"]').click()
            assert await saved() == '<p>Hello World</p>'
            selection = await page.evaluate('''() => {
              const s=window.getSelection();
              return {text:s.toString(), a:s.anchorOffset, f:s.focusOffset};
            }''')
            assert selection['text'] == 'World' and selection['a'] == 6 and selection['f'] == 11, selection
            await page.locator('#browser-test [data-quick-command="redo"]').click()
            assert '<strong>World</strong>' in await saved()
            print('PASS: toolbar command is undoable, selection restored')

            # Simulate IME composition as multiple intermediate input DOM states.
            await reset('<p>Start</p>')
            await page.evaluate('''() => {
              const canvas=document.querySelector('.az-azunt-editor-canvas');
              canvas.dispatchEvent(new CompositionEvent('compositionstart',{bubbles:true,data:''}));
              for (const part of ['ㅎ','하','한']) {
                canvas.querySelector('p').textContent='Start'+part;
                canvas.dispatchEvent(new InputEvent('input', {bubbles:true,inputType:'insertCompositionText',data:part,isComposing:true}));
              }
              canvas.dispatchEvent(new CompositionEvent('compositionend',{bubbles:true,data:'한'}));
              canvas.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:'한'}));
            }''')
            assert await saved() == '<p>Start한</p>'
            await page.keyboard.press('Control+z')
            assert await saved() == '<p>Start</p>', 'Composition must be one undo step'
            await page.keyboard.press('Control+y')
            assert await saved() == '<p>Start한</p>'
            print('PASS: IME composition grouped as one undo entry')

            # Checkbox state change and view/edit cycle remain undoable.
            await reset('<p><input type="checkbox" checked>Check</p>')
            cb = canvas.locator('input[type="checkbox"]')
            await cb.uncheck()
            assert 'checked' not in await saved()
            await page.locator('#browser-test [data-quick-command="undo"]').click()
            assert 'checked' in await saved()
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            assert await canvas.get_attribute('contenteditable') == 'false'
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            await page.locator('#browser-test [data-quick-command="redo"]').click()
            assert 'checked' not in await saved()
            print('PASS: checkbox changes and Edit/View round-trip preserve history')

            assert not errors, errors
            print('PASS: no unhandled page errors')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
