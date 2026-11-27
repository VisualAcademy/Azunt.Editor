"""Outer table resize and non-overlapping move grip, Chromium regression."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]

async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH','/usr/bin/chromium'), headless=True, args=['--no-sandbox','--disable-dev-shm-usage'])
        try:
            for host_handles in (False, True):
                page = await browser.new_page(viewport={'width':1100,'height':900})
                errors = []
                page.on('pageerror', lambda exc: errors.append(str(exc)))
                await page.set_content((ROOT/'tests/editor-adaptive-browser.html').read_text())
                if host_handles:
                    await page.evaluate('''() => document.getElementById('browser-test').insertAdjacentHTML('beforeend', '<button type="button" class="az-azunt-editor-table-move-handle" data-table-move-handle hidden>✥</button><button type="button" class="az-azunt-editor-table-column-handle" data-table-column-handle hidden></button><div class="az-azunt-editor-table-drop-marker" data-table-drop-marker hidden></div>')''')
                await page.add_style_tag(content=(ROOT/'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text())
                await page.add_style_tag(content='body{margin:40px}')
                await page.add_script_tag(content=(ROOT/'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text())
                root=page.locator('#browser-test'); canvas=root.locator('.az-azunt-editor-canvas')
                table=canvas.locator('table'); grip=root.locator('[data-table-move-handle]'); resize=root.locator('[data-table-column-handle]')
                async def html(): return await page.evaluate('AzuntEditor.getHtml("browser-test")')
                async def load(s): await page.evaluate('(s)=>AzuntEditor.setHtml("browser-test",s)',s)
                async def width(): return (await table.bounding_box())['width']
                async def ratios(): return await table.evaluate('t=>Array.from(t.rows[0].cells,c=>c.getBoundingClientRect().width/t.getBoundingClientRect().width)')
                async def edge_start():
                    b=await table.bounding_box(); x=b['x']+b['width']-2; y=b['y']+12
                    await page.mouse.move(x,y)
                    assert await resize.get_attribute('data-resize-axis')=='outer', (b,await resize.get_attribute('data-resize-axis'))
                    assert await resize.is_visible()
                    assert not await resize.evaluate("e=>e.classList.contains('az-is-dragging')")
                    await page.mouse.down()
                    assert await resize.evaluate("e=>e.classList.contains('az-is-dragging')")
                    return x,y
                async def drag(delta):
                    x,y=await edge_start(); await page.mouse.move(x+delta,y,steps=8); await page.mouse.up()
                    assert not await resize.evaluate("e=>e.classList.contains('az-is-dragging')")
                async def no_overlap():
                    await canvas.locator('td').first.click(position={'x':8,'y':8})
                    g=await grip.bounding_box(); t=await table.bounding_box()
                    assert g and (g['x']+g['width']<=t['x']-2 or g['y']+g['height']<=t['y']-2),(g,t)
                    assert await grip.evaluate('e=>document.elementFromPoint(e.getBoundingClientRect().x+e.offsetWidth/2,e.getBoundingClientRect().y+e.offsetHeight/2)===e')
                source='<p>Before</p><table style="width:100%;table-layout:fixed"><tr><td style="width:70%">a</td><td style="width:30%">b</td></tr><tr><td style="width:70%">c</td><td style="width:30%">d</td></tr></table><p>After</p>'
                for mode in ['Standard','BrowserDefault','CompactReport','BusinessDocument','Minimal']:
                    await page.evaluate('(m)=>AzuntEditor.setContentRendering("browser-test",m)',mode)
                    await load(source); await no_overlap()
                    original=await html(); w=await width(); r=await ratios()
                    await drag(-w*.32)
                    small=await width(); resized=await html(); rr=await ratios()
                    assert w*.64<small<w*.72,(mode,w,small)
                    assert abs(r[0]-rr[0])<.035,(mode,r,rr)
                    assert 'width: 100%' not in resized and 'a</td>' in resized, resized
                    await canvas.press('Control+z'); assert await html()==original,(mode,await html(),original)
                    await canvas.press('Control+y'); assert await html()==resized
                    await page.evaluate('AzuntEditor.setMode("browser-test","View")'); assert abs(await width()-small)<3
                    await page.evaluate('AzuntEditor.setMode("browser-test","Edit")'); await load(resized); assert abs(await width()-small)<3
                    x,y=await edge_start(); await page.mouse.move(x+100,y,steps=5); await page.keyboard.press('Escape'); await page.mouse.up()
                    assert await html()==resized and abs(await width()-small)<3
                    await drag(w); assert abs(await width()-w)<3,(mode,await width(),w)
                    await page.mouse.move(2,2); assert not await resize.is_visible()
                    print('PASS',host_handles,mode,'grip, width, ratios, undo/redo, reload, cancel, max')
                await page.add_style_tag(content='body{margin:0} #browser-test{overflow:hidden}')
                await load(source); await no_overlap()
                print('PASS clipped gutter fallback')
                await load('<p>Before</p><table style="width:100%"><tr><td>a</td></tr><tr><td>b</td></tr></table><p>After</p>')
                w=await width(); await drag(-w*.4); assert abs(await width()-w*.6)<5
                for value in ['0%','100.1%','-10%','80px','calc(50%)','999%']:
                    cleaned=await page.evaluate('(v)=>AzuntEditor.sanitizeHtml(`<table style="width:${v};position:fixed"><tr><td>A</td></tr></table>`)',value)
                    assert 'width:' not in cleaned and 'position:' not in cleaned,cleaned
                assert not errors,errors
                await page.close()
        finally:
            await browser.close()

if __name__=='__main__': asyncio.run(main())
