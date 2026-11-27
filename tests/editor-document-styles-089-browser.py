"""Chromium fixture: document style menus, live HTML/history, and instance isolation."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text()
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
DOC = '<h2>Report</h2><p>Plain text</p><p style="font-size:20px;color:#123456">Explicit</p><table style="width:90%"><thead><tr><th>Report #</th><th>Department</th></tr></thead><tbody><tr><td>DL2026000061</td><td style="padding:7px;font-size:18px">Accounting</td></tr></tbody></table>'
STYLES = {'Standard': ('15px','8px'), 'BrowserDefault': ('16px','1px'), 'CompactReport': ('13px','3px'), 'BusinessDocument': ('14px','6px'), 'Minimal': ('14px','4px')}

async def main():
    async with async_playwright() as pw:
        opts = {'headless': True}
        path = os.getenv('AZUNT_CHROMIUM_PATH') or ('/usr/bin/chromium' if Path('/usr/bin/chromium').exists() else None)
        if path: opts['executable_path'] = path
        if os.name != 'nt': opts['args'] = ['--no-sandbox','--disable-dev-shm-usage']
        browser = await pw.chromium.launch(**opts)
        try:
            for scheme in ('light','dark'):
                page = await browser.new_page(viewport={'width':1200,'height':900},color_scheme=scheme)
                errors = []
                page.on('pageerror', lambda error: errors.append(str(error)))
                await page.set_content(HTML)
                # Mirror the opt-in C# toolbar markup in the static harness.
                await page.evaluate('''styles=>{const root=document.querySelector('#browser-test');
                    const copy=root.cloneNode(true);copy.id='other-test';document.body.append(copy);
                    const group=document.createElement('div');group.className='az-azunt-editor-group';group.dataset.groupLabel='Document style';
                    const select=document.createElement('select');select.className='az-azunt-editor-select';select.dataset.select='documentStyle';
                    Object.keys(styles).forEach(key=>select.add(new Option(key,key)));group.append(select);root.querySelector('[data-classic-groups]').prepend(group);
                    root.dataset.contentRendering='compact-report';
                    window.styleEvents=[];window.styleCallbacks=[];root.addEventListener('azunt:content-rendering-changed',e=>styleEvents.push(e.detail.rendering));
                }''',STYLES)
                await page.add_style_tag(content=CSS)
                await page.add_script_tag(content=JS)
                for id in ('browser-test','other-test'):
                    await page.evaluate('([id,html])=>AzuntEditor.setHtml(id,html)',[id,DOC])
                await page.evaluate('''()=>AzuntEditor.mount('browser-test',{invokeMethodAsync(method,...args){
                    if(method==='OnContentRenderingChanged') styleCallbacks.push(args[0]);return Promise.resolve();}})''')
                root=page.locator('#browser-test'); canvas=root.locator('.az-azunt-editor-canvas'); selector=root.locator('[data-select=documentStyle]')
                before=await page.evaluate('AzuntEditor.getHtml("browser-test")')
                assert await selector.input_value()=='CompactReport'
                assert await page.evaluate('AzuntEditor.getContentRendering("other-test")')=='Standard'
                assert await page.locator('#other-test [data-select=documentStyle]').count()==0
                await root.locator('[data-azunt-menu=format]').click()
                for style,(size,padding) in STYLES.items():
                    await selector.select_option(style)
                    assert await selector.input_value()==style
                    assert await page.evaluate('AzuntEditor.getContentRendering("browser-test")')==style
                    actual=await canvas.evaluate('el=>[getComputedStyle(el).fontSize,getComputedStyle(el.querySelector("td")).paddingTop,getComputedStyle(el.querySelectorAll("p")[1]).fontSize,getComputedStyle(el.querySelectorAll("td")[1]).fontSize]')
                    assert actual==[size,padding,'20px','18px'],(scheme,style,actual)
                    assert await page.evaluate('AzuntEditor.getHtml("browser-test")')==before
                assert await page.evaluate('styleEvents')==list(STYLES)
                assert await page.evaluate('styleCallbacks')==list(STYLES)
                await page.keyboard.press('Escape')
                await page.evaluate('AzuntEditor.setContentRendering("browser-test","CompactReport")')
                assert await selector.input_value()=='CompactReport'
                await canvas.locator('p').first.click();await page.keyboard.press('End');await page.keyboard.type('Z')
                await page.evaluate('AzuntEditor.setContentRendering("browser-test","Minimal")')
                await page.keyboard.press('Control+z')
                assert await page.evaluate('AzuntEditor.getHtml("browser-test")')==before
                await page.keyboard.press('Control+y')
                assert 'Plain textZ' in await page.evaluate('AzuntEditor.getHtml("browser-test")')
                await page.keyboard.press('Control+z')
                await page.evaluate('AzuntEditor.setMode("browser-test","View")')
                assert await page.evaluate('AzuntEditor.getContentRendering("browser-test")')=='Minimal'
                assert await canvas.evaluate('el=>getComputedStyle(el).fontSize')=='14px'
                await page.evaluate('AzuntEditor.setMode("browser-test","Edit")')
                assert await selector.input_value()=='Minimal'
                await page.evaluate('AzuntEditor.setShowMenuBar("browser-test",false)')
                await root.locator('[data-azunt-menu=more]').click();await selector.select_option('CompactReport')
                await page.keyboard.press('Escape')
                await page.evaluate('AzuntEditor.setToolbarMode("browser-test","Classic")')
                await selector.select_option('BusinessDocument')
                assert await selector.input_value()=='BusinessDocument'
                assert await page.evaluate('AzuntEditor.getHtml("browser-test")')==before
                assert await page.evaluate('AzuntEditor.getContentRendering("other-test")')=='Standard'
                assert not errors, errors
                print('PASS:',scheme,'five styles, Format/More/Classic, HTML, inline styles, history and isolation')
                await page.close()
        finally: await browser.close()

if __name__=='__main__': asyncio.run(main())
