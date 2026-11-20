"""Azunt.Editor 0.7.0 — safe clipboard paste and block placement regressions.
Run: python tests/editor-paste-070-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')
TINY = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+8qV0AAAAASUVORK5CYII='

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium',
            headless=True, args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page()
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            await page.set_content(HTML)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            canvas = page.locator('#browser-test .az-azunt-editor-canvas')

            async def set_text_and_caret(text, offset):
                await page.evaluate('''([text, offset]) => {
                  AzuntEditor.setHtml('browser-test', '<p>' + text + '</p>');
                  const node = document.querySelector('.az-azunt-editor-canvas p').firstChild;
                  const range = document.createRange();
                  range.setStart(node, offset); range.collapse(true);
                  const selection = window.getSelection();
                  selection.removeAllRanges(); selection.addRange(range);
                }''', [text, offset])
                await canvas.focus()

            async def paste(html, text=''):
                await page.evaluate('''([html, text]) => {
                  const clipboardData = new DataTransfer();
                  clipboardData.setData('text/html', html);
                  clipboardData.setData('text/plain', text);
                  const canvas = document.querySelector('.az-azunt-editor-canvas');
                  canvas.dispatchEvent(new ClipboardEvent('paste', {
                      bubbles: true, cancelable: true, clipboardData
                  }));
                }''', [html, text])

            async def read():
                return await page.evaluate('AzuntEditor.getHtml("browser-test")')

            # Genuine CF_HTML, including source document metadata, fragment markers
            # and dangerous head/script content. Word-like CSS becomes safe semantic tags.
            await set_text_and_caret('LeftRight', 4)
            html = '''Version:1.0\r\nStartHTML:0000000000\r\n<html><head><title>ShouldNotPaste</title>
              <style>.x{color:red}</style></head><body>OutsideFragment
              <!--StartFragment--><p class="MsoNormal"><span style="font-weight:700;font-style:italic; text-decoration: underline; color:#123456">Word</span> <font face="Georgia" color="#112233">Font</font></p>
              <p><a href="javascript:alert(1)" onclick="alert(2)">Unsafe link</a><script>alert(3)</script></p>
              <!--EndFragment-->AfterFragment</body></html>'''
            await paste(html, 'Word Font Unsafe link')
            result = await read()
            assert 'Left' in result and 'Right' in result and 'Word' in result, result
            assert 'ShouldNotPaste' not in result and 'OutsideFragment' not in result and 'AfterFragment' not in result, result
            assert all(v not in result for v in ('<script', 'onclick=', 'javascript:', 'MsoNormal', 'font-weight:', 'StartHTML:', 'mso-')), result
            assert '<strong>' in result and '<em>' in result and '<u>' in result, result
            assert 'font-family: Georgia' in result and 'rgb(17, 34, 51)' in result, result
            structure = await canvas.evaluate('''el => Array.from(el.children).map(x => x.tagName)''')
            assert structure == ['P', 'P', 'P', 'P'], structure
            assert await canvas.locator('p p, p table, p div').count() == 0, result
            # Undo/redo must restore before-paste and after-paste snapshots.
            await page.locator('[data-quick-command="undo"]').click()
            assert 'Word' not in await read(), await read()
            await page.locator('[data-quick-command="redo"]').click()
            assert 'Word' in await read(), await read()

            # Paragraph and table insertion never produces an invalid nested
            # block inside a root paragraph; surrounding caret text is retained.
            await set_text_and_caret('BeforeAfter', 6)
            await paste('<table><tbody><tr><td>T1</td><td>T2</td></tr></tbody></table><p>Next</p>', 'T1 T2 Next')
            value = await read()
            assert 'Before' in value and 'After' in value and '<table>' in value and '<p>Next</p>' in value, value
            assert await canvas.locator('p table, p p').count() == 0, value
            assert await canvas.locator('table tr td').count() == 2
            assert await canvas.locator('p').first.inner_text() == 'Before'
            assert await canvas.locator('p').last.inner_text() == 'After'
            # Newly accepted h1/h4 headings must also split around pasted blocks.
            await page.evaluate('''() => {
              AzuntEditor.setHtml('browser-test', '<h1>TopBottom</h1>');
              const node = document.querySelector('.az-azunt-editor-canvas h1').firstChild;
              const range = document.createRange(); range.setStart(node, 3); range.collapse(true);
              const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
            }''')
            await paste('<p>Middle</p>')
            heading_result = await read()
            assert '<h1>Top</h1><p>Middle</p><h1>Bottom</h1>' == heading_result, heading_result
            # Form controls and legitimate raster images survive, unsafe images do not.
            await set_text_and_caret('Original', 8)
            await paste(f'<p><input type="checkbox" checked>Done <img src="{TINY}" alt="img"></p>'
                        '<p><input type="password"><img src="data:image/svg+xml;base64,AAAA" onerror="evil()"></p>')
            result = await read()
            assert '<input type="checkbox" checked' in result and '<img ' in result, result
            assert 'password' not in result and 'svg+xml' not in result and 'onerror' not in result, result
            # The same sanitized HTML can be viewed and edited without loss.
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            assert await canvas.get_attribute('contenteditable') == 'false'
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert await read() == result, (await read(), result)

            # Text-only sources preserve explicit newlines; script-only rich HTML
            # falls back to plain text without running scripts.
            await set_text_and_caret('ABCDE', 2)
            await paste('', 'First\nSecond')
            assert 'First<br>Second' in await read(), await read()
            await set_text_and_caret('ABC', 1)
            await paste('<script>throw new Error("injected")</script>', 'Plain text fallback')
            result = await read()
            assert 'Plain text fallback' in result and '<script' not in result, result

            # Document/source HTML handling remains unchanged (paste-specific transform only).
            direct = await page.evaluate('''() => AzuntEditor.sanitizeHtml(
                '<p><span style="font-weight:bold">Direct</span></p>')''')
            assert 'font-weight' not in direct and '<strong>' not in direct, direct
            # No unexpected new event wiring when returning from View.
            await page.evaluate('''() => { window.exportHits=[];
              AzuntEditor.registerCommand({id:'exportPdf', label:'Download PDF', group:'export',
                execute: () => window.exportHits.push(1)}, 'browser-test'); }''')
            await page.locator('[data-azunt-menu="export"]').first.click()
            await page.locator('[data-menu-panel] [data-custom-command="exportPdf"]').click()
            assert await page.evaluate('window.exportHits') == [1]
            assert not errors, errors
            print('PASS: CF_HTML, Word inline styles, safe paste, block splitting, table, forms, images, Undo/Redo, View, Export')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
