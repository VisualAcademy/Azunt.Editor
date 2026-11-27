"""Regression: invisible edge hit targets, column/row drag and cancellation."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text()
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
DOC = '<p>Before</p><table><tr><td>Alpha</td><td>Beta</td><td>Gamma</td></tr><tr><td>Delta</td><td>Epsilon</td><td>Zeta</td></tr></table><p>After</p>'

async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH','/usr/bin/chromium'), args=['--no-sandbox'])
        try:
            for host_handles in (False, True):
                page = await browser.new_page(viewport={'width':1160,'height':900})
                errors=[]
                page.on('pageerror',lambda exc: errors.append(str(exc)))
                await page.set_content(HTML)
                if host_handles:
                    await page.evaluate('''() => document.querySelector('#browser-test').insertAdjacentHTML('beforeend',
                      '<button type="button" class="az-azunt-editor-table-move-handle" data-table-move-handle hidden>✥</button>'+ 
                      '<button type="button" class="az-azunt-editor-table-column-handle" data-table-column-handle hidden></button>'+ 
                      '<div class="az-azunt-editor-table-drop-marker" data-table-drop-marker hidden></div>')''')
                await page.add_style_tag(content=CSS)
                await page.add_script_tag(content=JS)
                root=page.locator('#browser-test'); canvas=root.locator('.az-azunt-editor-canvas')
                cell=canvas.locator('td').first; grip=root.locator('[data-table-column-handle]'); move=root.locator('[data-table-move-handle]')
                async def html(): return await page.evaluate('AzuntEditor.getHtml("browser-test")')
                async def edge(axis):
                    b=await cell.bounding_box()
                    x,y=(b['x']+b['width']-2,b['y']+b['height']/2) if axis=='column' else (b['x']+b['width']/2,b['y']+b['height']-2)
                    await page.mouse.move(x,y)
                    assert await grip.is_visible(),axis
                    assert await grip.get_attribute('data-resize-axis')==axis
                    assert await grip.evaluate('el=>getComputedStyle(el).backgroundImage')=='none'
                    assert await grip.evaluate('el=>getComputedStyle(el).cursor')==('col-resize' if axis=='column' else 'row-resize')
                    return x,y
                await page.evaluate('(h)=>AzuntEditor.setHtml("browser-test",h)',DOC)
                before=await html()
                await cell.hover(); assert not await grip.is_visible() and not await move.is_visible()
                await edge('column'); assert not await move.is_visible() # hover does not select
                await cell.click(); assert not await grip.is_visible() and await move.is_visible()
                await edge('row'); await cell.hover(); assert not await grip.is_visible()
                await edge('column'); await page.mouse.move(2,2); assert not await grip.is_visible()
                assert await html()==before
                for axis in ('column','row'):
                    x,y=await edge(axis)
                    await page.mouse.down()
                    assert await grip.evaluate('el=>el.classList.contains("az-is-dragging")')
                    assert 'gradient' in await grip.evaluate('el=>getComputedStyle(el).backgroundImage')
                    await page.mouse.move(x+(35 if axis=='column' else 0),y+(32 if axis=='row' else 0),steps=5)
                    await page.mouse.up()
                    assert 'gradient' not in await grip.evaluate('el=>getComputedStyle(el).backgroundImage')
                    saved=await html(); assert ('table-layout: fixed' if axis=='column' else 'height:') in saved,saved
                    assert all(v in saved for v in ('Alpha','Beta','Gamma','Delta','Epsilon','Zeta'))
                    await page.keyboard.press('Control+z'); assert await html()==before,(axis,await html())
                    await page.keyboard.press('Control+y'); assert await html()==saved
                    await page.keyboard.press('Control+z'); assert await html()==before
                    x,y=await edge(axis); await page.mouse.down(); await page.mouse.move(x+30,y+30,steps=4)
                    await page.keyboard.press('Escape'); await page.mouse.up(); assert await html()==before,(axis,await html())
                    assert not await grip.evaluate('el=>el.classList.contains("az-is-dragging")')
                # Keyboard resizing stays available from the selected table's move grip.
                await cell.click(); await move.focus(); await page.keyboard.press('ArrowRight')
                assert 'width:' in await html()
                await canvas.focus(); await page.keyboard.press('Control+z'); assert await html()==before
                await cell.click(); await move.focus(); await page.keyboard.press('ArrowDown')
                assert 'height:' in await html()
                await canvas.focus(); await page.keyboard.press('Control+z'); assert await html()==before
                # Scroll away from a stationary pointer.
                await page.evaluate('document.body.style.paddingBottom="1400px"')
                await edge('column'); await page.evaluate('window.scrollBy(0, 450)')
                await page.wait_for_function('document.querySelector("[data-table-column-handle]").hidden')
                await page.evaluate('window.scrollTo(0,0)')
                # Release outside; pointer capture keeps resizing alive until release.
                x,y=await edge('column'); await page.mouse.down(); await page.mouse.move(x+40,y-70,steps=6); await page.mouse.up()
                assert not await grip.is_visible() and 'width:' in await html()
                await page.keyboard.press('Control+z'); assert await html()==before
                x,y=await edge('row'); await page.mouse.down(); await page.mouse.move(x,y+35,steps=4)
                await grip.evaluate('el=>{for(let i=1;i<10;i++)if(el.hasPointerCapture(i))el.releasePointerCapture(i)}')
                await page.mouse.move(x,y+36); await page.mouse.up(); assert await html()==before
                # Row height survives serialization, reload and browser View/Edit.
                x,y=await edge('row'); await page.mouse.down(); await page.mouse.move(x,y+34,steps=4); await page.mouse.up(); saved=await html()
                await page.evaluate('(h)=>AzuntEditor.setHtml("browser-test",h)',saved); assert await html()==saved
                await page.evaluate('AzuntEditor.setMode("browser-test","View")'); assert 'height:' in await html(); assert not await grip.count() or not await grip.is_visible()
                await page.evaluate('AzuntEditor.setMode("browser-test","Edit")'); assert 'height:' in await html()
                await page.evaluate('AzuntEditor.setHtml("browser-test", `<table><tr style="height:9999px"><td colspan="2">Merged</td><td>X</td></tr></table>`)')
                assert 'height:' not in await html()
                b=await cell.bounding_box(); await page.mouse.move(b['x']+b['width']-2,b['y']+b['height']/2); assert not await grip.is_visible()
                await edge('row'); assert not errors,errors
                print('PASS:',host_handles,'hover, both axes, undo/redo, Escape, outside release, lost capture, reload, View, merged guard')
                await page.close()
        finally: await browser.close()

if __name__=='__main__': asyncio.run(main())
