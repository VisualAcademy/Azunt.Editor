"""Native rendering vs. a plain Chromium document; no .NET host required."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text()
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
DOCUMENT = ('<h2>Report heading</h2><p>Plain paragraph</p>'
            '<p style="font-size:12px;font-family:Arial;color:#123456;text-align:center">Explicit formatting</p>'
            '<table><thead><tr><th>Report #</th><th>Department</th></tr></thead>'
            '<tbody><tr><td>DL2026000061</td><td>Accounting</td></tr></tbody></table>'
            '<table><tr><td style="border-color:#123456;border-width:2px">Border</td></tr></table>'
            '<ul><li>First item</li></ul><p><a href="https://example.com">Link</a></p>')

# Compare computed properties, not the viewport-dependent page height.
MEASURE = '''el => {
 const read=(node,keys)=>Object.fromEntries(keys.map(k=>[k,getComputedStyle(node)[k]]));
 const cell=el.querySelector('td'); const th=el.querySelector('th');
 return { body:read(el,['fontSize','lineHeight','color','overflowWrap']),
 h:read(el.querySelector('h2'),['fontSize','fontWeight','marginTop','marginBottom','lineHeight']),
 p:read(el.querySelector('p'),['fontSize','marginTop','marginBottom','color']),
 explicit:read(el.querySelectorAll('p')[1],['fontSize','fontFamily','color','textAlign']),
 table:read(el.querySelector('table'),['borderCollapse','borderSpacing','marginTop','marginBottom']),
 cell:read(cell,['paddingTop','paddingLeft','minWidth','borderTopStyle','verticalAlign','fontSize']),
 header:read(th,['fontWeight','backgroundColor','textAlign']),
 border:read(el.querySelectorAll('table')[1].querySelector('td'),['borderTopWidth','borderTopColor','borderTopStyle']),
 list:read(el.querySelector('ul'),['paddingLeft','listStyleType','marginTop']),
 link:read(el.querySelector('a'),['color','textDecorationLine']),
 tableWidth:el.querySelector('table').getBoundingClientRect().width };
}'''

async def main():
    async with async_playwright() as pw:
        opts = {'headless': True}
        if os.getenv('AZUNT_CHROMIUM_PATH'):
            opts['executable_path'] = os.environ['AZUNT_CHROMIUM_PATH']
        elif Path('/usr/bin/chromium').exists():
            opts['executable_path'] = '/usr/bin/chromium'
        if os.name != 'nt': opts['args'] = ['--no-sandbox', '--disable-dev-shm-usage']
        browser = await pw.chromium.launch(**opts)
        try:
            for scheme in ('light','dark'):
                page = await browser.new_page(viewport={'width':1200,'height':900},color_scheme=scheme)
                errors=[]
                page.on('pageerror',lambda exc:errors.append(str(exc)))
                await page.set_content(HTML)
                await page.evaluate('''() => {const copy=document.querySelector('#browser-test').cloneNode(true);
                    copy.id='native-test';copy.dataset.contentRendering='browser-default';
                    document.body.append(copy); }''')
                await page.add_style_tag(content=CSS)
                # Common host reset; native mode should not pick up these properties.
                await page.add_style_tag(content='body{font:14px/1.5 Arial;color:#456} h2{font-size:20px;margin:0} p{margin:0;color:#456} table{border-collapse:collapse} th,td{padding:9px}')
                await page.add_script_tag(content=JS)
                for id in ('browser-test','native-test'):
                    await page.evaluate('([id,html])=>AzuntEditor.setHtml(id,html)',[id,DOCUMENT])
                native=page.locator('#native-test .az-azunt-editor-canvas')
                standard=page.locator('#browser-test .az-azunt-editor-canvas')
                before=await page.evaluate('AzuntEditor.getHtml("native-test")')
                await page.evaluate('''html=>{const frame=document.createElement('iframe');frame.id='plain';
                     frame.style.width='1000px';frame.srcdoc='<!doctype html><html><head><style>body{color-scheme:light}</style></head><body>'+html+'</body></html>';document.body.append(frame)}''',before)
                ref=page.frame_locator('#plain').locator('body')
                await ref.locator('td').first.wait_for()
                plain=await ref.evaluate(MEASURE)
                actual=await native.evaluate(MEASURE)
                for key in plain:
                    assert actual[key]==plain[key],(scheme,key,actual[key],plain[key])
                assert (await standard.evaluate(MEASURE))['cell']['paddingLeft']=='8px'
                await page.evaluate('AzuntEditor.setContentRendering("native-test","Standard")')
                assert await native.evaluate(MEASURE)==await standard.evaluate(MEASURE)
                await page.evaluate('AzuntEditor.setContentRendering("native-test","BrowserDefault")')
                assert await native.evaluate(MEASURE)==plain
                assert before==await page.evaluate('AzuntEditor.getHtml("native-test")')
                assert await standard.evaluate('(el)=>el.parentElement.hasAttribute("data-content-rendering")') is False
                # Native mode changes no document data, and the existing edit history survives.
                await native.locator('p').first.click()
                await page.keyboard.press('End')
                await page.keyboard.type('Z')
                await page.evaluate('AzuntEditor.setContentRendering("native-test","Standard")')
                await page.evaluate('AzuntEditor.setContentRendering("native-test","BrowserDefault")')
                await page.keyboard.press('Control+z')
                assert before==await page.evaluate('AzuntEditor.getHtml("native-test")')
                await page.keyboard.press('Control+y')
                assert 'Plain paragraphZ' in await page.evaluate('AzuntEditor.getHtml("native-test")')
                await page.keyboard.press('Control+z')
                await page.evaluate('AzuntEditor.setMode("native-test","View")')
                assert await native.evaluate(MEASURE)==plain
                assert await native.get_attribute('contenteditable')=='false'
                await page.evaluate('AzuntEditor.setMode("native-test","Edit")')
                assert await native.evaluate(MEASURE)==plain
                assert before==await page.evaluate('AzuntEditor.getHtml("native-test")')
                assert await page.evaluate('''()=>{try{AzuntEditor.setContentRendering('native-test','invalid');return false}catch(e){return true}}''')
                # Native tables keep the existing resize and undo contract.
                await native.locator('td').first.click()
                grip=page.locator('#native-test [data-table-column-handle]')
                assert await grip.is_visible()
                b=await grip.bounding_box()
                await page.mouse.move(b['x']+5,b['y']+5)
                await page.mouse.down()
                await page.mouse.move(b['x']+20,b['y']+5,steps=4)
                await page.mouse.up()
                assert 'table-layout: fixed' in await page.evaluate('AzuntEditor.getHtml("native-test")')
                await native.locator('td').first.click()
                await page.keyboard.press('Control+z')
                assert before==await page.evaluate('AzuntEditor.getHtml("native-test")')
                await native.locator('p').last.hover()
                assert not await grip.is_visible()
                assert not errors,errors
                print('PASS:',scheme,'plain browser comparison, inline formatting, default isolation, Edit/View, HTML and Undo/Redo')
                await page.close()
        finally: await browser.close()

if __name__=='__main__': asyncio.run(main())
