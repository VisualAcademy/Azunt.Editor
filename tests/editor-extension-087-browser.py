"""Selection-aware host commands, isolated bookmarks and safe insertion in Chromium."""
import asyncio
import base64
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text()

async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path=os.environ.get('AZUNT_CHROMIUM_PATH', '/usr/bin/chromium'), args=['--no-sandbox'] if os.name != 'nt' else [])
        page = await browser.new_page(viewport={'width': 1200, 'height': 900})
        errors = []
        page.on('pageerror', lambda e: errors.append(str(e)))
        try:
            await page.set_content(HTML)
            await page.evaluate('''() => {
              const clone = document.getElementById('browser-test').cloneNode(true);
              clone.id = 'second'; document.body.append(clone);
              const dialog = document.createElement('dialog'); dialog.id='picker';
              dialog.innerHTML='<input id="picker-input"><button id="close-picker">Close</button>';
              document.body.append(dialog);
              document.querySelector('#close-picker').onclick=()=>dialog.close();
            }''')
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            await page.evaluate('''() => {
              window.id='browser-test'; window.api=AzuntEditor;
              window.selectAt=(id,a,b=a)=>{ const c=document.querySelector('#'+id+' .az-azunt-editor-canvas'); c.focus(); const r=document.createRange(); const t=c.querySelector('p').firstChild; r.setStart(t,a);r.setEnd(t,b);const s=getSelection();s.removeAllRanges();s.addRange(r); };
              window.reset=()=>{api.setMode(id,'Edit');api.setHtml(id,'<p>Before middle after</p>');selectAt(id,7,13);};
              reset(); api.registerCommand({id:'picker',label:'Pick content',group:'Insert',placement:'Toolbar',captureSelection:true,execute:ctx=>{window.token=ctx.selectionToken;document.getElementById('picker').showModal();}},id);
            }''')
            await page.locator('#browser-test [data-quick-custom] [data-custom-command="picker"]').click()
            assert await page.evaluate('typeof token === "string"')
            await page.locator('#picker-input').fill('external picker')
            assert not await page.evaluate('api.insertText(id,token,"blocked while modal open")')
            await page.locator('#close-picker').click()
            assert await page.evaluate('api.insertHtml(id,token,"<strong>selected</strong><script>window.bad=1</script>")')
            assert await page.evaluate('api.getHtml(id)') == '<p>Before <strong>selected</strong> after</p>'
            assert not await page.evaluate('api.insertText(id,token,"duplicate")')
            assert not await page.evaluate('Boolean(window.bad)')
            await page.keyboard.press('Control+z')
            assert await page.evaluate('api.getHtml(id)') == '<p>Before middle after</p>'
            await page.keyboard.press('Control+y')
            assert '<strong>selected</strong>' in await page.evaluate('api.getHtml(id)')
            print('PASS: modal focus, sanitized HTML, selection replacement, single use, undo/redo')
            assert await page.evaluate('''()=>{reset();const t=api.saveSelection(id);selectAt('second',0);return !api.insertText('second',t,'wrong') && api.insertText(id,t,'<tag>');}''')
            assert await page.evaluate('api.getHtml(id)') == '<p>Before &lt;tag&gt; after</p>'
            assert await page.evaluate('api.getHtml("second")') == '<p>Initial</p>'
            assert await page.evaluate('''()=>{reset();const a=api.saveSelection(id);const b=api.saveSelection(id);return !api.releaseSelection(id,a)&&api.releaseSelection(id,b)&&!api.restoreSelection(id,b);}''')
            assert await page.evaluate('''()=>{reset();const t=api.saveSelection(id);api.setHtml(id,'<p>Before middle after</p>');return !api.insertText(id,t,'stale');}''')
            assert await page.evaluate('''()=>{reset();const t=api.saveSelection(id);document.querySelector('#'+id+' p').firstChild.data='Changed';return !api.insertText(id,t,'stale');}''')
            assert await page.evaluate('''()=>{reset();const t=api.saveSelection(id);api.setMode(id,'View');api.setMode(id,'Edit');return !api.insertText(id,t,'stale');}''')
            assert await page.evaluate('''()=>{reset();const t=api.saveSelection(id);api.unmount(id);api.mount(id);return !api.insertText(id,t,'stale');}''')
            assert await page.evaluate('''()=>{reset();const t=api.saveSelection(id);document.querySelector('#'+id+' .az-azunt-editor-canvas').style.color='red';document.querySelector('#'+id+' p').style.color='#ff0000';return !api.insertText(id,t,'stale');}''')
            print('PASS: cross-editor isolation, cancel, replacement, external mutation, mode and remount invalidation')
            assert await page.evaluate('''()=>{reset();const t=api.saveSelection(id);return !api.insertImage(id,t,'javascript:alert(1)') && api.insertImage(id,t,'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jK1cAAAAASUVORK5CYII=','Example',120);}''')
            assert '<img' in await page.evaluate('api.getHtml(id)') and 'width="120"' in await page.evaluate('api.getHtml(id)')
            await page.evaluate('''()=>{reset();window.calls=[];api.mount(id,{invokeMethodAsync:(...args)=>{calls.push(args);return Promise.resolve()}});api.setCommands(id,[{id:'pick',label:'Pick',placement:'Toolbar',captureSelection:true},{id:'exportDoc',label:'Export',placement:'Toolbar'}]);}''')
            await page.locator('#browser-test [data-quick-custom] [data-custom-command="pick"]').click()
            assert await page.evaluate('calls[0][0] === "OnCustomCommandWithSelection" && calls[0][1] === "pick" && typeof calls[0][2] === "string"')
            assert await page.evaluate('api.insertText(id,calls[0][2],"Blazor")')
            await page.locator('#browser-test [data-quick-custom] [data-custom-command="exportDoc"]').click()
            assert await page.evaluate('calls.some(c=>c[0]==="OnCustomCommand"&&c[1]==="exportDoc")')
            await page.evaluate('api.unmount(id);document.getElementById(id).dataset.readOnly="true";api.mount(id);')
            assert await page.locator('#browser-test [data-quick-custom] [data-custom-command="picker"]').is_disabled()
            assert await page.evaluate('api.saveSelection(id) === null')
            assert not errors, errors
            print('PASS: image validation, Blazor selection callback, legacy callback, read-only')
            module = (ROOT / 'samples/Azunt.Web/wwwroot/js/extension-demo.js').read_bytes()
            await page.evaluate('''async url=>{
              window.pickerModule=await import(url);
              document.querySelector('#picker').innerHTML='<button data-pick="image">Image</button><button data-pick="cancel">Cancel</button>';
              window.pickResult='pending'; pickerModule.pickContent(document.querySelector('#picker')).then(v=>pickResult=v);
            }''', 'data:text/javascript;base64,' + base64.b64encode(module).decode())
            await page.locator('#picker [data-pick="image"]').click()
            await page.wait_for_function('pickResult === "image"')
            await page.evaluate('()=>{pickResult="pending"; pickerModule.pickContent(document.querySelector("#picker")).then(v=>pickResult=v);}')
            await page.keyboard.press('Escape')
            await page.wait_for_function('pickResult === null')
            assert not errors, errors
            print('PASS: host demo native dialog choice and Escape cancellation')
        finally:
            await browser.close()

if __name__ == '__main__': asyncio.run(main())
