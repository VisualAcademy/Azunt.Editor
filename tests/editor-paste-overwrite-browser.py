"""Full selection + paste overwrites the entire document without ghost headings.

Run: python tests/editor-paste-overwrite-browser.py
Uses Chromium/Playwright; MVC, Razor Pages and Blazor share this JS engine.
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')

async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                                   args=['--no-sandbox','--disable-dev-shm-usage'])
        try:
            page = await browser.new_page()
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            await page.set_content(HTML)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            await page.evaluate('AzuntEditor.mount("browser-test")')
            canvas = page.locator('#browser-test .az-azunt-editor-canvas')

            async def saved():
                return await page.evaluate('AzuntEditor.getHtml("browser-test")')

            async def load(html):
                await page.evaluate('(html) => AzuntEditor.setHtml("browser-test", html)', html)
                await canvas.focus()

            async def select_all():
                await canvas.focus()
                await page.keyboard.press('Control+a')
                assert (await page.evaluate('window.getSelection().toString()')).strip(), 'Failed to select document'

            async def paste(html='', text=''):
                await page.evaluate('''([html, text]) => {
                    const d = new DataTransfer();
                    d.setData('text/html', html); d.setData('text/plain', text);
                    document.querySelector('#browser-test .az-azunt-editor-canvas').dispatchEvent(
                        new ClipboardEvent('paste', {bubbles:true, cancelable:true, clipboardData:d}));
                }''', [html, text])

            original = '<h2><strong>Old title</strong></h2><p>Old paragraph.</p>'
            copied = '<h2 style="margin-left: 0px;">Razor Pages demo</h2><p>Try saving this content.</p>'
            await load(original)
            await select_all()
            await paste(copied, 'Razor Pages demo\nTry saving this content.')
            assert await saved() == copied, await saved()
            assert await canvas.locator(':scope > *').count() == 2
            await page.keyboard.type('!')
            assert await saved() == copied.replace('content.</p>', 'content.!</p>'), await saved()
            print('PASS: Ctrl+A then rich paste replaces all headings/paragraphs and keeps caret in final paragraph')

            await page.keyboard.press('Control+z')
            assert await saved() == copied, await saved()
            await page.keyboard.press('Control+z')
            assert await saved() == original, await saved()
            await page.keyboard.press('Control+y')
            assert await saved() == copied, await saved()
            print('PASS: Undo restores previous complete document; redo restores pasted document')

            # Repeated cross-editor overwrites must not accumulate ghost blocks.
            for _ in range(3):
                await select_all()
                await paste(copied, 'Razor Pages demo\nTry saving this content.')
                assert await saved() == copied, await saved()
            print('PASS: Repeat overwrites never accumulate empty wrappers')

            # Replacing source documents that already include empty leading/trailing blocks.
            await load('<h2><br></h2><h2>Old heading</h2><p>Old paragraph</p><p><br></p>')
            await select_all()
            await paste(copied)
            assert await saved() == copied, await saved()
            print('PASS: Entire-document replacement removes old empty heading and paragraph')

            await load(original)
            await select_all()
            await paste(text='First\nSecond')
            assert await saved() == '<p>First<br>Second</p>', await saved()
            await page.keyboard.type('!')
            assert await saved() == '<p>First<br>Second!</p>', await saved()
            print('PASS: Plain-text full replacement resets inherited heading formatting')

            await load(original)
            await select_all()
            await paste(html='<strong>Inline content</strong>', text='Inline content')
            assert await saved() == '<p><strong>Inline content</strong></p>', await saved()
            print('PASS: Inline-only HTML full replacement is wrapped in a paragraph')

            for content in ('<ul><li>One</li><li>Two</li></ul>',
                            '<table><tbody><tr><td>Cell</td></tr></tbody></table>'):
                await load(original)
                await select_all()
                await paste(html=content)
                assert await saved() == content, await saved()
            print('PASS: Lists and tables fully replace selected content without phantom blocks')

            # Genuine blanks in the clipboard content remain untouched.
            await load(original)
            await select_all()
            intentional = '<p><br></p><h2>Heading</h2><p>Body</p><p><br></p>'
            await paste(intentional)
            assert await saved() == intentional, await saved()
            print('PASS: Blank lines from the clipboard remain intact')

            # Partial selection should still edit only selected text within a block.
            await load('<h2>KEEP</h2><p>prefix replace suffix</p><p>AFTER</p>')
            await page.evaluate('''() => {
                const text = document.querySelector('#browser-test .az-azunt-editor-canvas p').firstChild;
                const range = document.createRange();range.setStart(text, 7);range.setEnd(text, 14);
                const selection = window.getSelection();selection.removeAllRanges();selection.addRange(range);
            }''')
            await paste(text='NEW')
            assert await saved() == '<h2>KEEP</h2><p>prefix NEW suffix</p><p>AFTER</p>', await saved()
            print('PASS: Partial selection replacement retains surrounding document')

            # All nonempty text is selected, but an unselected empty paragraph
            # must not be discarded as if the entire document were selected.
            await load('<h2>Only heading</h2><p><br></p>')
            await page.evaluate('''() => {
                const node = document.querySelector('#browser-test .az-azunt-editor-canvas h2').firstChild;
                const range = document.createRange(); range.selectNodeContents(node);
                const selection = getSelection();selection.removeAllRanges();selection.addRange(range);
            }''')
            await paste(text='New text')
            assert await saved() == '<h2>New text</h2><p><br></p>', await saved()
            print('PASS: Unselected empty trailing paragraph survives partial text replacement')

            # Media outside the selection must never be removed by text-only selection.
            image = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+8qV0AAAAASUVORK5CYII='
            await load(f'<h2>Title</h2><p><img src="{image}" alt="pixel"></p>')
            await page.evaluate('''() => {
                const node=document.querySelector('#browser-test .az-azunt-editor-canvas h2').firstChild;
                const range=document.createRange();range.selectNodeContents(node);
                const selection=getSelection();selection.removeAllRanges();selection.addRange(range);
            }''')
            await paste(html='<strong>Replacement</strong>',text='Replacement')
            assert '<img ' in await saved(), await saved()
            print('PASS: Image not in selection remains untouched')

            # Real Chromium Ctrl+C / Ctrl+V between two mounted editor instances.
            # Clipboard HTML may contain browser-generated inline styles, but it
            # must not leave empty heading/paragraph siblings in the target.
            await page.evaluate('''() => {
                const source = document.getElementById('browser-test');
                const target = source.cloneNode(true);
                target.id = 'blazor-target'; source.after(target);
                AzuntEditor.mount('blazor-target');
                AzuntEditor.setHtml('browser-test', '<h2>Razor Pages demo</h2><p>Try saving this content.</p>');
                AzuntEditor.setHtml('blazor-target', '<h2>Blazor demo</h2><p>Replace me.</p>');
            }''')
            source_canvas = page.locator('#browser-test .az-azunt-editor-canvas')
            target_canvas = page.locator('#blazor-target .az-azunt-editor-canvas')
            await source_canvas.click()
            await page.keyboard.press('Control+a')
            await page.keyboard.press('Control+c')
            await target_canvas.click()
            await page.keyboard.press('Control+a')
            await page.keyboard.press('Control+v')
            native_html = await page.evaluate('AzuntEditor.getHtml("blazor-target")')
            assert native_html.count('<h2') == 1 and native_html.count('<p') == 1, native_html
            assert await target_canvas.locator(':scope > *').count() == 2, native_html
            assert 'Razor Pages demo' in native_html and 'Try saving this content.' in native_html, native_html
            print('PASS: Native Ctrl+C / Ctrl+A / Ctrl+V between two editor instances')

            assert not errors, errors
            print('PASS: No browser exceptions')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
