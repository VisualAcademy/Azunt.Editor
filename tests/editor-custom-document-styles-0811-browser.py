"""Custom style registration, validation, isolation and shared print rendering."""
from pathlib import Path
import os
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
js=(root/'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
css=(root/'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
fixture=(root/'tests/editor-adaptive-browser.html').read_text()
style={'id':'office-report','name':'Office Report','fontFamily':'Arial','fontSize':12,'tableFontSize':11,'lineHeight':1.25,'cellPaddingVertical':2,'cellPaddingHorizontal':4,'textColor':'#123456'}
with sync_playwright() as p:
    opts={'headless':True,'args':['--no-sandbox','--disable-dev-shm-usage']}
    exe=os.getenv('AZUNT_TEST_CHROMIUM') or ('/usr/bin/chromium' if Path('/usr/bin/chromium').exists() else None)
    if exe: opts['executable_path']=exe
    browser=p.chromium.launch(**opts)
    page=browser.new_page(viewport={'width':1280,'height':900})
    errors=[]; page.on('pageerror',lambda e:errors.append(str(e)))
    page.set_content(fixture)
    page.evaluate('''style=>{const root=document.querySelector('#browser-test');const other=root.cloneNode(true);other.id='other-test';document.body.append(other);
        root.dataset.documentStyles=JSON.stringify([style]);root.dataset.documentStyleId='custom:office-report';root.dataset.baseContentRendering='compact-report';root.dataset.contentRendering='custom-document';
        const group=document.createElement('div');group.className='az-azunt-editor-group';group.dataset.groupLabel='Document style';
        const select=document.createElement('select');select.className='az-azunt-editor-select';select.dataset.select='documentStyle';
        ['Standard','BrowserDefault','CompactReport','BusinessDocument','Minimal'].forEach(key=>select.add(new Option(key,key)));group.append(select);root.querySelector('[data-classic-groups]').prepend(group);
        window.callbacks=[];window.events=[];root.addEventListener('azunt:document-style-changed',e=>events.push(e.detail.styleId));}''',style)
    page.add_style_tag(content=css);page.add_script_tag(content=js)
    page.evaluate('''()=>AzuntEditor.mount('browser-test',{invokeMethodAsync(method,...args){callbacks.push([method,...args]);return Promise.resolve();}})''')
    page.evaluate('''()=>AzuntEditor.setHtml('browser-test','<p>Plain text</p><p style="font-size:20px">Explicit</p><table style="width:65%"><tbody><tr><td>Data</td><td>Two</td></tr></tbody></table>')''')
    before=page.evaluate('AzuntEditor.getHtml("browser-test")'); canvas=page.locator('#browser-test .az-azunt-editor-canvas')
    metrics='e=>[getComputedStyle(e).fontSize,getComputedStyle(e).fontFamily,getComputedStyle(e).color,getComputedStyle(e.querySelector("td")).fontSize,getComputedStyle(e.querySelector("td")).paddingTop,getComputedStyle(e.querySelectorAll("p")[1]).fontSize]'
    expected=canvas.evaluate(metrics)
    assert expected==['12px','Arial, sans-serif','rgb(18, 52, 86)','11px','2px','20px'],expected
    assert page.evaluate('AzuntEditor.getDocumentStyleId("browser-test")')=='custom:office-report'
    assert page.evaluate('AzuntEditor.getContentRendering("browser-test")')=='CompactReport'
    assert page.evaluate('AzuntEditor.getDocumentStyleId("other-test")')=='Standard'
    page.evaluate('AzuntEditor.setMode("browser-test","View")');assert canvas.evaluate(metrics)==expected
    page.evaluate('AzuntEditor.setMode("browser-test","Edit")');assert canvas.evaluate(metrics)==expected
    page.evaluate('AzuntEditor.setToolbarMode("browser-test","Classic")')
    selector=page.locator('#browser-test [data-select=documentStyle]');assert selector.input_value()=='custom:office-report'
    selector.select_option('Minimal');selector.select_option('custom:office-report');assert canvas.evaluate(metrics)==expected
    assert page.evaluate('events')==['Minimal','custom:office-report']
    assert ['OnDocumentStyleChanged','custom:office-report','Minimal'] in page.evaluate('callbacks')
    with page.expect_popup() as event: page.locator('#browser-test [data-command=preview]').click()
    popup=event.value;popup.wait_for_selector('#azunt-print-controls button:enabled');doc=popup.locator('.az-azunt-print-page .az-azunt-editor-canvas')
    assert doc.evaluate(metrics)==expected
    popup.emulate_media(media='print');assert doc.evaluate(metrics)==expected
    popup.emulate_media(media='screen');popup.locator('select[aria-label="Print layout"]').select_option('compact');popup.locator('select[aria-label="Print layout"]').select_option('document');assert doc.evaluate(metrics)==expected
    popup.close()
    assert page.evaluate('AzuntEditor.getHtml("browser-test")')==before
    assert page.evaluate('''s=>{try{AzuntEditor.registerDocumentStyles('browser-test',[s,{...s,textColor:'red;display:none'}]);return false;}catch{return true;}}''',style)
    assert canvas.evaluate(metrics)==expected
    page.evaluate('''s=>AzuntEditor.registerDocumentStyles('browser-test',[{...s,fontSize:14}])''',style);assert canvas.evaluate(metrics)[0]=='14px'
    page.evaluate('AzuntEditor.registerDocumentStyles("browser-test",[])');assert page.evaluate('AzuntEditor.getDocumentStyleId("browser-test")')=='Minimal'
    assert canvas.evaluate(metrics)[0]=='14px'
    assert page.evaluate('AzuntEditor.getHtml("browser-test")')==before
    assert not errors,errors
    browser.close();print('PASS: custom initial markup, selection, validation, isolation, edit/view, preview/print, fallback and unchanged HTML')
