"""Viewport height, zoom, selection, output and resize regression in Chromium."""
from pathlib import Path
import os
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
with sync_playwright() as p:
    browser=p.chromium.launch(executable_path=os.getenv('AZUNT_TEST_CHROMIUM','/usr/bin/chromium'),headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1200,'height':900})
    errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
    page.set_content((root/'tests/editor-adaptive-browser.html').read_text().replace('<textarea data-azunt-source', '<div class="az-azunt-editor-image-resize-handle" data-image-resize-handle hidden tabindex="0"></div><textarea data-azunt-source'))
    page.evaluate('''()=>{const r=document.querySelector('#browser-test'); const other=r.cloneNode(true); other.id='other-test'; document.body.append(other);
    const g=document.createElement('div');g.className='az-azunt-editor-group';g.dataset.groupLabel='View options';g.innerHTML='<select data-select="viewHeightMode"><option>AutoGrow</option><option>Fixed</option></select><select data-select="viewZoom"><option>75</option><option>100</option><option>150</option></select>';r.querySelector('[data-classic-groups]').prepend(g);
    r.dataset.viewHeightMode='fixed';r.dataset.viewHeight='300';r.dataset.viewZoom='100';r.style.cssText='--azunt-view-height:300px;--azunt-view-zoom:100%;--azunt-view-scale:1';window.events=[];window.calls=[];r.addEventListener('azunt:view-options-changed',e=>events.push(e.detail));}''')
    page.add_style_tag(content=(root/'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text())
    page.add_script_tag(content=(root/'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text())
    page.evaluate('''()=>AzuntEditor.mount('browser-test',{invokeMethodAsync(m,...args){calls.push([m,...args]);return Promise.resolve();}})''')
    canvas=page.locator('#browser-test .az-azunt-editor-canvas');editor=page.locator('#browser-test');
    def load(html): page.evaluate('s=>AzuntEditor.setHtml("browser-test",s)',html)
    def opts(mode,zoom): page.evaluate('([m,z])=>AzuntEditor.setViewOptions("browser-test",m,300,z)',[mode,zoom])
    def html(): return page.evaluate('AzuntEditor.getHtml("browser-test")')
    def height(): return canvas.bounding_box()['height']
    def info(): return canvas.evaluate('e=>[e.clientHeight,e.scrollHeight,e.clientWidth,e.scrollWidth]')
    load('<p>Top</p>'+''.join(f'<p>Line {i}</p>' for i in range(80)))
    original=html(); assert abs(height()-300)<2 and info()[1]>info()[0]
    canvas.evaluate('e=>e.scrollTop=e.scrollHeight'); assert canvas.evaluate('e=>e.scrollTop')>0
    for z in (50,75,100,125,150,200):
        opts('Fixed',z); assert abs(height()-300)<3,(z,height(),info()); assert info()[1]>info()[0]; assert html()==original
        assert canvas.bounding_box()['width']<=editor.bounding_box()['width']+2,(z,canvas.bounding_box(),editor.bounding_box())
    opts('AutoGrow',100); assert height()>1000;assert html()==original
    assert page.evaluate('AzuntEditor.getViewOptions("other-test").zoomPercent')==100
    opts('Fixed',150); page.evaluate('AzuntEditor.setMode("browser-test","View")'); assert abs(height()-300)<3; assert html()==original
    page.evaluate('AzuntEditor.setMode("browser-test","Edit")'); assert abs(height()-300)<3
    page.evaluate('AzuntEditor.setToolbarMode("browser-test","Classic")');editor.locator('[data-select=viewZoom]').select_option('75');assert page.evaluate('events.at(-1).zoomPercent')==75;assert ['OnViewOptionsChanged','Fixed',75] in page.evaluate('calls')
    editor.locator('[data-command=source]').click(); assert abs(editor.locator('[data-azunt-source]').bounding_box()['height']-300)<3;editor.locator('[data-command=source]').click()
    with page.expect_popup() as ev: editor.locator('[data-command=preview]').click()
    popup=ev.value;popup.wait_for_selector('#azunt-print-controls button:enabled');assert popup.locator('.az-azunt-editor-canvas').evaluate('e=>getComputedStyle(e).zoom')=='1';assert 'Line 79' in popup.locator('.az-azunt-editor-canvas').inner_text();popup.close()
    # Same saved percentage change at different zoom levels. The drag distance is visual pixels.
    for z in (75,100,150):
        opts('Fixed',z);load('<table style="width:100%;table-layout:fixed"><tr><td style="width:70%">a</td><td style="width:30%">b</td></tr><tr><td>c</td><td>d</td></tr></table>');canvas.evaluate('e=>e.scrollTop=0');table=canvas.locator('table'); b=table.bounding_box();x=b['x']+b['width']-2;y=b['y']+12
        page.mouse.move(x,y);assert editor.locator('[data-table-column-handle]').get_attribute('data-resize-axis')=='outer',(z,b)
        before=html();page.mouse.down();page.mouse.move(x-b['width']*.25,y,steps=6);page.mouse.up();after=html(); ratio=table.bounding_box()['width']/b['width'];assert .71<ratio<.79,(z,ratio,after)
        canvas.press('Control+z');assert html()==before
        canvas.press('Control+y');assert html()==after
    # Row height and image width are stored in document pixels, not zoomed pixels.
    for z in (75,150):
        opts('Fixed',z);load('<table style="width:100%"><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>');canvas.evaluate('e=>e.scrollTop=0');row=canvas.locator('tr').first;b=row.bounding_box();x=b['x']+b['width']*.25;y=b['y']+b['height']-1
        page.mouse.move(x,y);assert editor.locator('[data-table-column-handle]').get_attribute('data-resize-axis')=='row'
        page.mouse.down();page.mouse.move(x,y+30*z/100,steps=5);page.mouse.up();assert abs(row.bounding_box()['height']-b['height']-30*z/100)<4,(z,b,row.bounding_box())
            # 1x1 raster scaled by explicit width; no remote asset dependencies.
        load('<p><img width="100" src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5C8AAAAASUVORK5CYII=" alt="Sample"></p>');canvas.evaluate('e=>e.scrollTop=0')
        image=canvas.locator('img');image.click();handle=editor.locator('[data-image-resize-handle]');h=handle.bounding_box();assert h,(z,image.bounding_box());x=h['x']+h['width']/2;y=h['y']+h['height']/2
        page.mouse.move(x,y);page.mouse.down();page.mouse.move(x+40*z/100,y,steps=5);page.mouse.up();assert abs(int(image.get_attribute('width'))-140)<=2,(z,image.get_attribute('width'))
    # Fullscreen must override fixed viewport height without losing the zoom.
    opts('Fixed',150);editor.locator('[data-command=fullscreen]').click();assert editor.evaluate('e=>e.classList.contains("az-azunt-editor-fullscreen")')
    assert canvas.bounding_box()['height'] <= editor.bounding_box()['height'] and canvas.bounding_box()['height']>400
    editor.locator('[data-command=fullscreen]').click();assert abs(height()-300)<3
    assert not errors,errors
    print('PASS: fixed/auto height, zoom 50–200%, per-editor isolation, source, edit/view, menu callback, print isolation, zoom-aware table resize and undo')
    browser.close()
