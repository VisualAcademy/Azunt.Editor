"""Azunt.Editor 0.7.0, fourth pass: image insert/save/restore lifetime regressions.
Run: python tests/editor-image-persistence-070-browser.py
Tests shared JavaScript in local Chromium; no production network or .NET host.
"""
import asyncio
import base64
import os
import struct
import zlib
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'
HTML = ROOT / 'tests/editor-features-browser.html'


def png():
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    return (b'\x89PNG\r\n\x1a\n' +
            chunk(b'IHDR', struct.pack('>IIBBBBB', 16, 12, 8, 2, 0, 0, 0)) +
            chunk(b'IDAT', zlib.compress((b'\x00' + b'\xff\xaa\x00' * 16) * 12)) +
            chunk(b'IEND', b''))


async def editor(page):
    html = HTML.read_text().replace('<script src="../src/Azunt.Editor/wwwroot/js/azunt-editor.js"></script>', '')
    html = html.replace('<button type="button" data-command="image-delete">',
                        '<button type="button" data-command="image-original">Original</button>'
                        '<select data-select="imageInsertSize"><option value="original">Original</option>'
                        '<option value="480">480 px</option></select>'
                        '<button type="button" data-command="image-delete">')
    html = html.replace('<textarea data-azunt-source hidden>',
                        '<div class="az-azunt-editor-image-resize-handle" data-image-resize-handle hidden '
                        'tabindex="0" role="button" aria-label="Resize"></div>'
                        '<textarea data-azunt-source hidden>')
    await page.set_content(html)
    await page.add_style_tag(content=CSS.read_text())
    await page.add_script_tag(content=JS.read_text())


async def main():
    async with async_playwright() as p:
        options = {'headless': True}
        if Path('/usr/bin/chromium').exists():
            options['executable_path'] = '/usr/bin/chromium'
        if os.name != 'nt':
            options['args'] = ['--no-sandbox', '--disable-dev-shm-usage']
        browser = await p.chromium.launch(**options)
        try:
            page = await browser.new_page(viewport={'width': 1150, 'height': 850})
            errors = []
            page.on('pageerror', lambda err: errors.append(str(err)))
            await editor(page)
            img = {'name': 'one.png', 'mimeType': 'image/png', 'buffer': png()}
            upload = page.locator('#browser-test [data-azunt-images]')
            await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>BeforeAfter</p>")')
            await page.evaluate('''() => {
                const text = document.querySelector('.az-azunt-editor-canvas p').firstChild;
                const range = document.createRange(); range.setStart(text, 6); range.collapse(true);
                const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
                window.dispatchEvent(new Event('selectionchange'));
                // Delayed FileReader simulates large Base64 images and busy browsers.
                const nativeRead = FileReader.prototype.readAsDataURL;
                FileReader.prototype.readAsDataURL = function(file) {
                    setTimeout(() => nativeRead.call(this, file), 100);
                };
            }''')
            await upload.set_input_files(img)
            # Focus and selection change during the pending read must not relocate image.
            await page.evaluate('''() => {
                const text = document.querySelector('.az-azunt-editor-canvas p').firstChild;
                const range = document.createRange(); range.setStart(text, text.length); range.collapse(true);
                const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
            }''')
            await page.locator('.az-azunt-editor-canvas img[alt="one.png"]').wait_for()
            saved = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert saved.index('Before') < saved.index('one.png') < saved.index('After'), saved[:300]
            assert 'az-azunt-editor-image-selected' not in saved
            await page.evaluate('(html) => AzuntEditor.setHtml("browser-test", html)', saved)
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == saved
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == saved
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == saved
            print('PASS: async image caret, sanitized HTML restore, Edit/View roundtrip')

            await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Replace me</p>")')
            await upload.set_input_files({'name': 'late.png', 'mimeType': 'image/png', 'buffer': png()})
            await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>New document</p>")')
            await page.wait_for_timeout(180)
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == '<p>New document</p>'
            # Mode switching while read is outstanding must not create stale callbacks.
            await upload.set_input_files({'name': 'obsolete.png', 'mimeType': 'image/png', 'buffer': png()})
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            await page.wait_for_timeout(180)
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == '<p>New document</p>'
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == '<p>New document</p>'
            # Source mode roundtrip also changes the document ownership of a
            # pending image read, even if the editor returns to Edit quickly.
            await upload.set_input_files({'name': 'old-source.png', 'mimeType': 'image/png', 'buffer': png()})
            await page.locator('#browser-test [data-command="source"]').click()
            await page.locator('#browser-test [data-command="source"]').click()
            await page.wait_for_timeout(180)
            assert 'old-source.png' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            print('PASS: canceled image import on setHtml, source mode and Edit/View switch')

            await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Files</p>")')
            await upload.set_input_files([
                {'name': 'first.png', 'mimeType': 'image/png', 'buffer': png()},
                {'name': 'second.png', 'mimeType': 'image/png', 'buffer': png()}
            ])
            await page.locator('.az-azunt-editor-canvas img[alt="second.png"]').wait_for()
            saved = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert saved.index('first.png') < saved.index('second.png'), saved[:400]
            await page.evaluate('''() => {
                const one = document.getElementById('browser-test');
                const two = one.cloneNode(true); two.id = 'another-editor';
                two.querySelector('.az-azunt-editor-canvas').innerHTML = '';
                two.querySelector('[data-azunt-value]').value = '<p>Independent</p>';
                one.after(two);
                AzuntEditor.mount('another-editor');
            }''')
            original = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            await page.locator('#another-editor [data-azunt-images]').set_input_files(img)
            await page.locator('#another-editor .az-azunt-editor-canvas img').wait_for()
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == original
            assert 'Independent' in await page.evaluate('AzuntEditor.getHtml("another-editor")')
            assert not errors, errors
            print('PASS: multiple file order and independent instances')

            # An unfinished pointer resize must be discarded before switching to
            # View, including any pending Blazor change notification.
            await page.evaluate('(html) => AzuntEditor.setHtml("browser-test", html)',
                                '<p><img src="data:image/png;base64,' + base64.b64encode(png()).decode() +
                                '" width="120" alt="Picture"></p>')
            await page.locator('#browser-test .az-azunt-editor-canvas img').click()
            await page.evaluate('''() => {
                const handle = document.querySelector('#browser-test [data-image-resize-handle]');
                const rect = handle.getBoundingClientRect();
                for (const [type, shift] of [['pointerdown',0],['pointermove',70]]) {
                    handle.dispatchEvent(new PointerEvent(type, {pointerId:79,pointerType:'touch',
                        bubbles:true,cancelable:true,button:0,
                        clientX:rect.left+6+shift,clientY:rect.top+6}));
                }
            }''')
            # Avoid relying on network image loading to size the fixture.
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            saved = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert 'width="120"' in saved, saved
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert 'width="120"' in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            print('PASS: interrupted resize is rolled back before View serialization')
        finally:
            await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
