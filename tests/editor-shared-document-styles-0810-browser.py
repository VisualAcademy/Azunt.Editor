"""Compare real editor/preview/print CSS, with the existing browser harness."""
from pathlib import Path
import os, re
from playwright.sync_api import sync_playwright
import fitz

root = Path(__file__).resolve().parents[1]
js = (root / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
css = (root / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
fixture = (root / 'tests/editor-adaptive-browser.html').read_text().replace('data-toolbar-mode="compact"', 'data-toolbar-mode="classic"')
styles = ['Standard', 'BrowserDefault', 'CompactReport', 'BusinessDocument', 'Minimal']
rows = ''.join(f'<tr><td>R-{i:06}</td><td>Accounting</td></tr>' for i in range(1,81))
html = ('<h2>Report</h2><p>Plain text</p><p style="font-size:20px;color:#123456">Explicit text</p>'
        '<table style="width:65%;table-layout:fixed"><thead><tr><th>Number</th><th>Department</th></tr></thead>'
        '<tbody>'+rows+'</tbody></table><p>End of report</p>')
metrics = '''el=>{
  const fields = {root:['fontFamily','fontSize','lineHeight','color'],
    h2:['fontSize','fontWeight','lineHeight','marginTop','marginBottom'],
    p:['fontSize','lineHeight','marginTop','marginBottom'],
    table:['fontSize','borderCollapse','marginTop','marginBottom','tableLayout'],
    td:['fontSize','lineHeight','paddingTop','paddingLeft','borderTopWidth','borderTopColor','verticalAlign'],
    th:['fontSize','fontWeight','backgroundColor','paddingTop']};
  return Object.fromEntries(Object.entries(fields).map(([tag,props])=>{
    const cs=getComputedStyle(tag==='root'?el:el.querySelector(tag));
    return [tag,props.map(p=>cs[p])];
  }));
}'''
with sync_playwright() as p:
    args = dict(headless=True, args=['--no-sandbox','--disable-dev-shm-usage'])
    exe = os.getenv('AZUNT_TEST_CHROMIUM') or ('/usr/bin/chromium' if Path('/usr/bin/chromium').exists() else None)
    if exe: args['executable_path'] = exe
    browser = p.chromium.launch(**args)
    page = browser.new_page(viewport={'width':1280,'height':900},color_scheme='light')
    errors=[]
    page.on('pageerror',lambda e: errors.append(str(e)))
    page.set_content(fixture)
    page.add_style_tag(content=css)
    page.add_script_tag(content=js)
    page.evaluate('AzuntEditor.mount("browser-test")')
    page.evaluate('(html)=>AzuntEditor.setHtml("browser-test",html)',html)
    original = page.evaluate('AzuntEditor.getHtml("browser-test")')
    counts={}
    for style in styles:
        page.evaluate('s=>AzuntEditor.setContentRendering("browser-test",s)',style)
        canvas=page.locator('#browser-test .az-azunt-editor-canvas')
        expected=canvas.evaluate(metrics)
        page.evaluate('AzuntEditor.setMode("browser-test","View")')
        assert canvas.evaluate(metrics)==expected,style
        page.evaluate('AzuntEditor.setMode("browser-test","Edit")')
        with page.expect_popup() as event:
            page.locator('[data-command="preview"]').click()
        popup=event.value
        popup.on('pageerror',lambda e: errors.append(str(e)))
        popup.wait_for_selector('#azunt-print-controls button:enabled')
        doc=popup.locator('.az-azunt-print-page .az-azunt-editor-canvas')
        assert popup.locator('select[aria-label="Print layout"]').input_value()=='document'
        assert doc.evaluate(metrics)==expected,(style,doc.evaluate(metrics),expected)
        assert doc.locator('p').nth(1).evaluate('e=>getComputedStyle(e).fontSize')=='20px'
        assert abs(doc.locator('table').evaluate('e=>e.getBoundingClientRect().width/e.parentElement.clientWidth')-.65)<.015
        popup.emulate_media(media='print')
        assert doc.evaluate(metrics)==expected,(style,'print',doc.evaluate(metrics),expected)
        pdf=popup.pdf(prefer_css_page_size=True, print_background=True)
        with fitz.open(stream=pdf,filetype='pdf') as output:
            text='\n'.join(pg.get_text() for pg in output)
            assert 'End of report' in text and 'Match document' not in text
            for i in range(1,81): assert text.count(f'R-{i:06}')==1,(style,i)
            for pg in output:
                if re.search(r'R-\d{6}',pg.get_text()): assert 'Number' in pg.get_text(),style
            counts[style]=len(output)
        popup.emulate_media(media='screen')
        popup.locator('select[aria-label="Print layout"]').select_option('compact')
        assert popup.locator('td').first.evaluate('e=>getComputedStyle(e).fontSize')=='12px'
        popup.locator('select[aria-label="Print layout"]').select_option('document')
        assert doc.evaluate(metrics)==expected
        assert page.evaluate('AzuntEditor.getHtml("browser-test")')==original
        assert page.evaluate('AzuntEditor.getContentRendering("browser-test")')==style
        popup.close()
    # The real Print command uses the same profile without requiring a preview click.
    page.evaluate('''()=>{const open=window.open;window.open=function(...args){
      const w=open.apply(window,args);w.print=()=>{w.__printCalls=(w.__printCalls||0)+1};return w;};}''')
    page.evaluate('AzuntEditor.setContentRendering("browser-test","CompactReport")')
    with page.expect_popup() as event: page.locator('[data-command="print"]').click()
    popup=event.value
    popup.wait_for_function('window.__printCalls===1')
    assert popup.locator('.az-azunt-print-page').get_attribute('data-print-profile')=='document'
    assert popup.locator('.az-azunt-editor-canvas').evaluate('e=>getComputedStyle(e).fontSize')=='13px'
    popup.close()
    # The print canvas is a light document even when Standard's editing UI is dark.
    page.emulate_media(color_scheme='dark')
    page.evaluate('AzuntEditor.setContentRendering("browser-test","CompactReport")')
    with page.expect_popup() as event: page.locator('[data-command="preview"]').click()
    popup=event.value
    popup.wait_for_selector('#azunt-print-controls button:enabled')
    assert popup.locator('.az-azunt-editor-canvas').evaluate('e=>getComputedStyle(e).color')=='rgb(32, 33, 36)'
    popup.close()
    # Missing/inaccessible stylesheet: no silent print with a mismatched style.
    page.evaluate('''()=>{for(const el of document.querySelectorAll('style')) if(el.textContent.includes('--azunt-document-size'))el.remove()}''')
    with page.expect_popup() as event: page.locator('[data-command="print"]').click()
    popup=event.value
    popup.wait_for_selector('#azunt-print-controls button:enabled')
    assert 'Document styles unavailable' in popup.locator('[role="status"]').inner_text()
    assert popup.locator('select[aria-label="Print layout"]').input_value()=='standard'
    assert popup.evaluate('window.__printCalls||0')==0
    assert not errors,errors
    print('PASS: five shared styles, edit/view/preview/print CSS, 65% width, 80 records, repeated headers, fallback:',counts)
    browser.close()
