"""Regression: original-size insertion and drag-to-resize must work in shared JS/CSS.
Run with: python tests/editor-images-browser.py (Python Playwright + Chromium).
"""
import asyncio
import struct
import zlib
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'
BASE = ROOT / 'tests/editor-features-browser.html'

def png(width, height):
    # Standalone RGB PNG generator; no Pillow or other third-party dependency.
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    scanline = b'\x00' + bytes((130, 180, 210)) * width
    return (b'\x89PNG\r\n\x1a\n' +
            chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) +
            chunk(b'IDAT', zlib.compress(scanline * height, 6)) +
            chunk(b'IEND', b''))

async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path='/usr/bin/chromium',
                                           args=['--no-sandbox', '--disable-dev-shm-usage'], headless=True)
        page = await browser.new_page(viewport={'width': 1150, 'height': 850})
        try:
            html = BASE.read_text().replace('<script src="../src/Azunt.Editor/wwwroot/js/azunt-editor.js"></script>', '')
            html = html.replace('<button type="button" data-command="image-delete">',
                                '<button type="button" data-command="image-original">Original</button>'
                                '<select data-select="imageInsertSize"><option value="original">Original size</option>'
                                '<option value="480">480 px</option></select>'
                                '<button type="button" data-command="image-delete">')
            html = html.replace('<textarea data-azunt-source hidden>',
                                '<div class="azunt-editor-image-resize-handle" data-image-resize-handle hidden '
                                'tabindex="0" role="button" aria-label="Resize selected image"></div>'
                                '<textarea data-azunt-source hidden>')
            await page.set_content(html)
            await page.add_style_tag(content=CSS.read_text())
            await page.add_script_tag(content=JS.read_text())
            await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Start</p>")')
            upload = page.locator('[data-azunt-images]')
            await upload.set_input_files({'name': 'original.png', 'mimeType': 'image/png', 'buffer': png(120, 80)})
            img = page.locator('.azunt-editor-canvas img').first
            await img.wait_for()
            await page.wait_for_function('document.querySelector(".azunt-editor-canvas img").complete')
            assert await img.get_attribute('width') is None, 'Default insertion must preserve native dimensions'
            rect = await img.bounding_box()
            assert 115 <= rect['width'] <= 125, rect
            assert 75 <= rect['height'] <= 85, rect
            handle = page.locator('[data-image-resize-handle]')
            assert await handle.is_visible(), 'Click-selected image should show its resize handle'
            pos = await handle.bounding_box()
            await page.mouse.move(pos['x'] + 9, pos['y'] + 9)
            await page.mouse.down()
            await page.mouse.move(pos['x'] + 99, pos['y'] + 9, steps=6)
            await page.mouse.up()
            resized = await img.bounding_box()
            assert resized['width'] > 190 and resized['width'] < 230, resized
            assert abs(resized['height'] / resized['width'] - 2/3) < .05, resized
            assert int(await img.get_attribute('width')) > 190
            saved = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert 'width="' in saved and 'data-image-resize-handle' not in saved, saved[:300]
            assert 'azunt-editor-image-selected' not in saved, saved[:300]
            await page.evaluate('(v) => AzuntEditor.setHtml("browser-test", v)', saved)
            img = page.locator('.azunt-editor-canvas img').first
            await img.click()
            assert int(await img.get_attribute('width')) > 190, 'Resized width lost on restore'
            await page.locator('[data-command="image-original"]').click()
            assert await img.get_attribute('width') is None, 'Original size button must clear forced width'
            await page.locator('[data-select="imageInsertSize"]').select_option('480')
            await upload.set_input_files({'name': 'large.png', 'mimeType': 'image/png', 'buffer': png(640, 320)})
            second = page.locator('.azunt-editor-canvas img[alt="large.png"]')
            await second.wait_for()
            await page.wait_for_function('Array.from(document.querySelectorAll(".azunt-editor-canvas img")).find(i=>i.alt==="large.png")?.complete')
            assert await second.get_attribute('width') == '480', 'Legacy 480 px insertion option missing'
            size = await second.bounding_box()
            assert 470 <= size['width'] <= 490, size
            # Resize handles are keyboard-operable as well as pointer-operable.
            await handle.focus()
            await handle.press('ArrowLeft')
            assert await second.get_attribute('width') == '470'
            print('PASS: original-size insertion, pointer drag, retained aspect ratio, HTML restore, 480px mode, keyboard resizing')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
