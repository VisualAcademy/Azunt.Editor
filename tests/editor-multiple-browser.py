"""Multiple editor engine + demo lifecycle regression (fixture markup, not a running .NET host)."""
import asyncio
import base64
import os
import runpy
from pathlib import Path
from bs4 import BeautifulSoup
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
fixture = runpy.run_path(str(ROOT / 'tests/editor-fullscreen-080-browser.py'))

def editor(id, name, text):
    root = BeautifulSoup(fixture['HTML'], 'html.parser').select_one('[data-azunt-editor]')
    root['id'] = id
    value = root.select_one('[data-azunt-value]')
    value['name'] = name
    value.string = '<p>' + text + '</p>' if text else ''
    return str(root)

async def main():
    markup = '<form id="multiple-editor-form">' + ''.join(editor('multi-'+n.lower(), n, n) for n in ['Document','Notes','Comments']) + '''
      <div id="multi-dynamic"></div><button type="button" id="multi-add">Add editor</button>
      <button type="button" id="multi-open">Open modal</button><button type="submit">Save all HTML</button>
      <dialog id="multi-modal" style="width:80vw"><div id="multi-modal-content"></div><button type="button" id="multi-close">Save modal and close</button></dialog></form>
      <template id="multi-template">''' + editor('multi-template-editor','Template','') + '</template><pre id="multi-saved"></pre>'
    module = (ROOT/'samples/Azunt.Web/wwwroot/js/multiple-editors.js').read_bytes()
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH','/usr/bin/chromium'), args=['--no-sandbox'])
        page = await browser.new_page(viewport={'width':1200,'height':900})
        errors=[]; page.on('pageerror',lambda error:errors.append(str(error)))
        await page.set_content(markup)
        await page.add_style_tag(content=fixture['CSS'])
        await page.add_script_tag(content=fixture['JS'])
        await page.evaluate('(url)=>import(url)', 'data:text/javascript;base64,'+base64.b64encode(module).decode())
        async def html(id): return await page.evaluate('(id)=>AzuntEditor.getHtml(id)',id)
        def root(id): return page.locator('#'+id)
        def canvas(id): return root(id).locator('.az-azunt-editor-canvas')
        async def type_at_end(id,text):
            await canvas(id).focus(); await page.keyboard.press('Control+End'); await page.keyboard.type(text)
        async def command(id, cmd): await root(id).locator('[data-quick-command="'+cmd+'"]').click()
        async def select_text(id):
            await canvas(id).evaluate('el=>{el.focus();const r=document.createRange();r.selectNodeContents(el.querySelector("p"));const s=getSelection();s.removeAllRanges();s.addRange(r)}')
        await type_at_end('multi-document',' A'); await type_at_end('multi-notes','B')
        assert await html('multi-document')=='<p>Document A</p>'
        assert await html('multi-notes')=='<p>NotesB</p>'
        await page.keyboard.press('Control+z'); assert await html('multi-notes')=='<p>Notes</p>'
        assert await html('multi-document')=='<p>Document A</p>'
        await page.keyboard.press('Control+y'); assert await html('multi-notes')=='<p>NotesB</p>'
        await select_text('multi-comments'); await command('multi-comments','bold')
        assert '<strong>Comments</strong>' in await html('multi-comments')
        assert await html('multi-notes')=='<p>NotesB</p>'
        print('PASS independent input, undo/redo, formatting')
        await page.evaluate('AzuntEditor.setShowMenuBar("multi-notes",false);AzuntEditor.setShowToolbarGroupLabels("multi-notes",false)')
        assert await root('multi-notes').locator('[data-compact-menu]').is_hidden()
        assert await root('multi-document').locator('[data-compact-menu]').is_visible()
        await root('multi-notes').locator('[data-azunt-menu="more"]').click()
        assert await root('multi-notes').locator('[data-menu-panel]').is_visible()
        await root('multi-comments').locator('[data-azunt-menu="more"]').evaluate('e=>e.click()')
        assert await root('multi-notes').locator('[data-menu-panel]').is_hidden()
        assert await root('multi-comments').locator('[data-menu-panel]').is_visible()
        await page.keyboard.press('Escape')
        assert await root('multi-comments').locator('[data-menu-panel]').is_hidden()
        print('PASS independent toolbar options, More ownership and Escape')
        await command('multi-document','fullscreen')
        await root('multi-notes').locator('[data-quick-command="fullscreen"]').evaluate('e=>e.click()')
        assert await page.locator('.az-azunt-editor-fullscreen').count()==1
        assert await root('multi-notes').evaluate('e=>e.classList.contains("az-azunt-editor-fullscreen")')
        await root('multi-notes').locator('[data-azunt-menu="more"]').click()
        await page.keyboard.press('Escape')
        assert await root('multi-notes').locator('[data-menu-panel]').is_hidden()
        assert await page.locator('.az-azunt-editor-fullscreen').count()==1
        await page.keyboard.press('Escape'); assert await page.locator('.az-azunt-editor-fullscreen').count()==0
        print('PASS exclusive fullscreen and two-stage Escape')
        await page.locator('#multi-add').click(); await type_at_end('multi-extra-1',' C')
        assert ' C' in await html('multi-extra-1')
        await page.get_by_role('button',name='Remove Extra1').click(); assert await root('multi-extra-1').count()==0
        await page.locator('#multi-add').click(); assert await html('multi-extra-2')=='<p>Extra editor</p>'
        for i in range(4):
            await page.locator('#multi-open').click()
            assert await page.locator('#multi-modal').evaluate('e=>e.open')
            assert await html('multi-ModalNotes')=='<p>Modal notes'+('X'*i)+'</p>'
            await type_at_end('multi-ModalNotes','X')
            assert await html('multi-ModalComments')=='<p>Modal comments</p>'
            await command('multi-ModalNotes','fullscreen')
            await page.keyboard.press('Escape')
            assert await page.locator('#multi-modal').evaluate('e=>e.open')
            assert await page.locator('.az-azunt-editor-fullscreen').count()==0
            await page.locator('#multi-close').click()
            assert await root('multi-ModalNotes').count()==0
        print('PASS dynamic add/remove and four modal mount/unmount cycles')
        await page.get_by_role('button',name='Save all HTML').click()
        import json
        saved=json.loads(await page.locator('#multi-saved').inner_text())
        assert saved['Document']=='<p>Document A</p>' and saved['Notes']=='<p>NotesB</p>'
        assert saved['ModalNotes']=='<p>Modal notesXXXX</p>' and 'Extra1' not in saved and 'Extra2' in saved
        # Simulated independent Blazor callbacks (not a real SignalR circuit).
        await page.evaluate("""() => {
            window.multiCallbacks = [];
            for (const id of ['multi-document','multi-notes']) {
                AzuntEditor.mount(id, {invokeMethodAsync: (method,...args) => {
                    window.multiCallbacks.push([id,method,...args]); return Promise.resolve();
                }});
            }
            AzuntEditor.setHtml('multi-document','<p>Blazor document</p>');
            AzuntEditor.setHtml('multi-notes','<p>Blazor notes</p>');
        }""")
        calls = await page.evaluate('window.multiCallbacks')
        assert any(c[:3]==['multi-document','OnHtmlChanged','<p>Blazor document</p>'] for c in calls), calls
        assert any(c[:3]==['multi-notes','OnHtmlChanged','<p>Blazor notes</p>'] for c in calls), calls
        assert all('Blazor notes' not in c[2] for c in calls if c[0]=='multi-document'), calls
        print('PASS simulated separate Blazor callbacks')
        assert not errors, errors
        print('PASS independent FormData and modal HTML snapshot; no browser page errors')
        await browser.close()

if __name__=='__main__': asyncio.run(main())
