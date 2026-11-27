"""Browser fixture for opt-in MVC form routing and unchanged stream download API."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text()
EDITOR = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
EXPORT = (ROOT / 'src/Azunt.Editor.Export/wwwroot/js/azunt-editor-export.js').read_text()
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()

async def main():
    async with async_playwright() as pw:
        opts = {'headless': True}
        path = os.getenv('AZUNT_CHROMIUM_PATH') or ('/usr/bin/chromium' if Path('/usr/bin/chromium').exists() else None)
        if path: opts['executable_path'] = path
        if os.name != 'nt': opts['args'] = ['--no-sandbox', '--disable-dev-shm-usage']
        browser = await pw.chromium.launch(**opts)
        try:
            page = await browser.new_page(accept_downloads=True, viewport={'width': 1200, 'height': 900})
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            await page.set_content(HTML)
            await page.evaluate('''() => {
                const first = document.querySelector('#browser-test');
                const second = first.cloneNode(true); second.id = 'second';
                const plain = first.cloneNode(true); plain.id = 'plain';
                for (const [root, formats, optIn] of [[first, ['Pdf','Docx','Xlsx'],true], [second,['Xlsx'],true], [plain,['Pdf'],false]]) {
                    const form = document.createElement('form'); form.id = 'form-' + root.id;
                    document.body.append(form); form.append(root);
                    const token = document.createElement('input'); token.type='hidden'; token.name='__RequestVerificationToken'; token.value='fixture-token'; form.append(token);
                    const group = document.createElement('span');
                    if (optIn) group.dataset.azuntExportEditor = root.id;
                    for (const format of formats) {
                        const button = document.createElement('button'); button.type='submit'; button.name='exportFormat'; button.value=format;
                        const labels={Pdf:['Pdf','PDF'],Docx:['Word','Word'],Xlsx:['Excel','Excel']};
                        button.dataset.azuntExportCommand='azuntExport'+labels[format][0]; button.textContent='Download '+labels[format][1]; group.append(button);
                    }
                    form.append(group);
                }
                window.posts=[];window.customCalls=0;
            }''')
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=EDITOR)
            await page.add_script_tag(content=EXPORT)
            await page.evaluate('''() => {
                AzuntEditorExport.connectForms(); AzuntEditorExport.connectForms();
                AzuntEditor.registerCommand({id:'quickSignature',label:'Quick Signature',group:'Tools',execute:()=>customCalls++},'browser-test');
                for(const form of document.forms) form.addEventListener('submit', e=>{
                    e.preventDefault(); const data=new FormData(form,e.submitter);
                    posts.push([form.id,data.get('exportFormat'),data.get('Html'),data.get('__RequestVerificationToken')]);
                });
                AzuntEditor.setHtml('browser-test','<p>Old HTML</p>');
                document.querySelector('#browser-test .az-azunt-editor-canvas p').textContent='Latest live HTML';
                AzuntEditor.setHtml('second','<p>Second editor</p>');
            }''')
            async def open_menu(id, name='export'):
                root = page.locator('#'+id)
                await root.locator('[data-azunt-menu='+name+']').click()
                return root
            for id, command, fmt, html in [('browser-test','Pdf','Pdf','Latest live HTML'),('browser-test','Word','Docx','Latest live HTML'),('browser-test','Excel','Xlsx','Latest live HTML'),('second','Excel','Xlsx','Second editor')]:
                root = await open_menu(id)
                await root.locator('[data-menu-panel] [data-custom-command=azuntExport'+command+']').click()
                post = await page.evaluate('posts.at(-1)')
                assert post == ['form-'+id,fmt,'<p>'+html+'</p>','fixture-token'], post
            root = await open_menu('second')
            assert await root.locator('[data-menu-panel] [data-custom-command=azuntExportPdf]').count() == 0
            await page.keyboard.press('Escape')
            root = await open_menu('plain')
            assert await root.locator('[data-menu-panel] [data-custom-command=azuntExportPdf]').count() == 0
            await page.keyboard.press('Escape')
            root = await open_menu('browser-test','tools')
            await root.locator('[data-menu-panel] [data-custom-command=quickSignature]').click()
            assert await page.evaluate('customCalls') == 1
            # Native validation blocks menu-triggered POST, exactly like clicking a real submitter.
            await page.evaluate('''() => {const input=document.createElement('input');input.required=true;input.id='required-field';document.querySelector('#form-browser-test').append(input);}''')
            root = await open_menu('browser-test')
            await root.locator('[data-menu-panel] [data-custom-command=azuntExportPdf]').click()
            assert await page.evaluate('posts.length') == 4
            await page.locator('#required-field').fill('ok')
            # Disabled real button cannot be submitted through the menu.
            await page.evaluate('document.querySelector("#form-browser-test button[value=Pdf]").disabled=true')
            root = await open_menu('browser-test')
            await root.locator('[data-menu-panel] [data-custom-command=azuntExportPdf]').click()
            assert await page.evaluate('posts.length') == 4
            await page.evaluate('AzuntEditorExport.disconnectForm(document.querySelector("#form-browser-test [data-azunt-export-editor]"))')
            root = await open_menu('browser-test')
            assert await root.locator('[data-menu-panel] [data-custom-command=azuntExportPdf]').count() == 0
            await page.keyboard.press('Escape')
            await page.evaluate('AzuntEditorExport.connectForms()')
            root = await open_menu('browser-test')
            assert await root.locator('[data-menu-panel] [data-custom-command=azuntExportPdf]').count() == 1
            await page.keyboard.press('Escape')
            await page.locator('#form-plain button[value=Pdf]').click()
            assert await page.evaluate('posts.at(-1).slice(0,2)') == ['form-plain','Pdf']
            for filename in ['Report.pdf','Report.docx','Report.xlsx']:
                async with page.expect_download() as info:
                    await page.evaluate('name=>AzuntEditorExport.download(name,{arrayBuffer:async()=>new Uint8Array([1,2,3]).buffer})', filename)
                download = await info.value
                assert download.suggested_filename == filename
                assert Path(await download.path()).read_bytes() == bytes([1,2,3])
            assert not errors, errors
            print('PASS: form export commands, live HTML, native validation, format isolation, unrelated commands, reconnect and stream downloads')
        finally:
            await browser.close()

if __name__ == '__main__': asyncio.run(main())
