"""Browser regression for image drop, sanitized rich-text drop, alt text, and touch resizing.
Run: python tests/editor-drop-browser.py
Requires Python Playwright and a local Chromium executable for browser tests only.
"""
import asyncio
import base64
from pathlib import Path
# Import the standalone PNG generator from the existing image regression test.
import importlib.util
image_spec = importlib.util.spec_from_file_location(
    'editor_images_browser', Path(__file__).with_name('editor-images-browser.py'))
image_module = importlib.util.module_from_spec(image_spec)
image_spec.loader.exec_module(image_module)
png = image_module.png
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'tests/editor-features-browser.html'
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'

async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(executable_path='/usr/bin/chromium',
            headless=True, args=['--no-sandbox','--disable-dev-shm-usage'])
        page = await browser.new_page(viewport={'width': 1100, 'height': 850})
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        try:
            html = BASE.read_text()
            html = html.replace('<script src="../src/Azunt.Editor/wwwroot/js/azunt-editor.js"></script>', '')
            html = html.replace('<input type="number" data-image-width',
                                '<input type="text" data-image-alt maxlength="200" aria-label="Image alternative text">'
                                '<input type="number" data-image-width')
            html = html.replace('<textarea data-azunt-source hidden>',
                                '<div class="az-azunt-editor-image-resize-handle" data-image-resize-handle hidden '
                                'tabindex="0" role="button" aria-label="Resize"></div>'
                                '<textarea data-azunt-source hidden>')
            await page.set_content(html)
            await page.add_style_tag(content=CSS.read_text())
            await page.add_script_tag(content=JS.read_text())
            await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Before<br>After</p>")')
            image_bytes = png(120, 80)
            await page.evaluate('''async ({payload}) => {
                const canvas = document.querySelector('.az-azunt-editor-canvas');
                const dt = new DataTransfer();
                const bytes = Uint8Array.from(atob(payload), c => c.charCodeAt(0));
                dt.items.add(new File([bytes], 'dropped.png', {type:'image/png'}));
                const rect = canvas.getBoundingClientRect();
                const options = {bubbles:true, cancelable:true, dataTransfer:dt,
                    clientX:rect.left+60,clientY:rect.top+22};
                const hover = new DragEvent('dragover', options);
                canvas.dispatchEvent(hover);
                if (!hover.defaultPrevented) throw new Error('File drag must be handled');
                if (!document.querySelector('.az-azunt-editor-image-drop-active'))
                  throw new Error('Drag feedback missing');
                const drop = new DragEvent('drop', options);
                canvas.dispatchEvent(drop);
                if (!drop.defaultPrevented) throw new Error('File drop must be handled');
            }''', {'payload':base64.b64encode(image_bytes).decode()})
            image = page.locator('.az-azunt-editor-canvas img[alt="dropped.png"]')
            await image.wait_for()
            assert await image.count() == 1
            assert await page.locator('.az-azunt-editor-image-drop-active').count() == 0
            # Image is a separate block, not a nested paragraph.
            assert await page.evaluate('''() => !Array.from(document.querySelectorAll('.az-azunt-editor-canvas p p')).length''')
            # Selecting and describing an image must survive HTML save and reload.
            await image.click()
            alt = page.locator('[data-image-alt]')
            assert await alt.input_value() == 'dropped.png'
            await alt.fill('Equipment photo 1')
            await alt.dispatch_event('change')
            assert await page.locator('.az-azunt-editor-canvas img').first.get_attribute('alt') == 'Equipment photo 1'
            html = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert 'alt="Equipment photo 1"' in html
            await page.evaluate('(html) => AzuntEditor.setHtml("browser-test", html)', html)
            assert await page.locator('.az-azunt-editor-canvas img[alt="Equipment photo 1"]').count() == 1

            # Incoming rich HTML must never bypass the editor sanitizer via native drop.
            await page.evaluate('''() => {
                const canvas = document.querySelector('.az-azunt-editor-canvas');
                const transfer = new DataTransfer();
                transfer.setData('text/html', '<p><strong>Keep</strong></p><script>window.unsafeDrop=1</script><p onmouseover="window.unsafeDrop=2">End</p>');
                transfer.setData('text/plain', 'Keep End');
                const r = canvas.getBoundingClientRect();
                const event = new DragEvent('drop', {bubbles:true,cancelable:true,dataTransfer:transfer,
                    clientX:r.left+35,clientY:r.top+35});
                canvas.dispatchEvent(event);
                if (!event.defaultPrevented) throw new Error('HTML drop was not intercepted');
            }''')
            html = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert '<strong>Keep</strong>' in html and 'End' in html
            assert 'onmouseover' not in html and '<script' not in html
            assert not await page.evaluate('Boolean(window.unsafeDrop)')

            # Unsupported MIME must be blocked and must not navigate away.
            image_count = await page.locator('.az-azunt-editor-canvas img').count()
            await page.evaluate('''() => {
                const canvas = document.querySelector('.az-azunt-editor-canvas');
                const transfer = new DataTransfer();
                transfer.items.add(new File(['text'], 'unsafe.svg', {type:'image/svg+xml'}));
                const r=canvas.getBoundingClientRect();
                const drop=new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:transfer,
                    clientX:r.left+20,clientY:r.top+20});
                canvas.dispatchEvent(drop);
                if(!drop.defaultPrevented) throw new Error('Unsupported file must not navigate');
            }''')
            await page.wait_for_timeout(100)
            assert await page.locator('.az-azunt-editor-canvas img').count() == image_count

            # Pointer Events support touch/pens with pointer capture; test touch path.
            image = page.locator('.az-azunt-editor-canvas img').first
            await image.click()
            handle = page.locator('[data-image-resize-handle]')
            await handle.wait_for(state='visible')
            before = await image.bounding_box()
            await page.evaluate('''() => {
                const handle = document.querySelector('[data-image-resize-handle]');
                const r=handle.getBoundingClientRect();
                for (const [eventType, delta] of [['pointerdown',0],['pointermove',50],['pointerup',50]]) {
                    handle.dispatchEvent(new PointerEvent(eventType,{bubbles:true,cancelable:true,
                        pointerId:77, pointerType:'touch',isPrimary:true,button:0,
                        clientX:r.left+7+delta,clientY:r.top+7}));
                }
            }''')
            after = await image.bounding_box()
            assert after['width'] > before['width'] + 30, (before,after)

            # Read-only editors must decline dropped files without changing the document.
            await page.evaluate('''() => {
                AzuntEditor.unmount('browser-test');
                const element=document.getElementById('browser-test');
                element.dataset.readOnly='true';
                element.querySelector('.az-azunt-editor-canvas').setAttribute('contenteditable','false');
                AzuntEditor.mount('browser-test');
            }''')
            before_readonly = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            await page.evaluate('''({payload}) => {
                const canvas=document.querySelector('.az-azunt-editor-canvas');
                const dt=new DataTransfer();
                const bytes=Uint8Array.from(atob(payload),c=>c.charCodeAt(0));
                dt.items.add(new File([bytes],'not-allowed.png',{type:'image/png'}));
                const r=canvas.getBoundingClientRect();
                const ev=new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:dt,
                    clientX:r.left+50,clientY:r.top+50});
                canvas.dispatchEvent(ev);
                if (!ev.defaultPrevented) throw new Error('Read-only drop must not navigate');
            }''', {'payload':base64.b64encode(image_bytes).decode()})
            await page.wait_for_timeout(100)
            assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == before_readonly
            assert not errors, errors
            print('PASS: local-image drop, drop position, HTML sanitization, rejected files, alt text restore, touch resize')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
