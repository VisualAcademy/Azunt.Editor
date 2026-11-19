"""Custom command API and DOM remount regression tests (Chromium)."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')

async def main():
    async with async_playwright() as pw:
        opts = {'headless': True}
        if os.getenv('AZUNT_CHROMIUM_PATH'):
            opts['executable_path'] = os.environ['AZUNT_CHROMIUM_PATH']
        elif Path('/usr/bin/chromium').exists():
            opts['executable_path'] = '/usr/bin/chromium'
        if os.name != 'nt':
            opts['args'] = ['--no-sandbox', '--disable-dev-shm-usage']
        browser = await pw.chromium.launch(**opts)
        page = await browser.new_page(viewport={'width': 1200, 'height': 900})
        page.set_default_timeout(4000)
        errors=[]
        page.on('pageerror', lambda err: errors.append(str(err)))
        try:
            await page.set_content(HTML)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            await page.evaluate('''() => {
              window.calls = [];
              AzuntEditor.registerCommand({id:'saveReport',label:'Global Save',group:'export',placement:'menu',
                execute:()=>window.calls.push('global')});
              AzuntEditor.registerCommand({id:'saveReport',label:'Local Save',group:'export',placement:'toolbar',icon:'S',
                execute:()=>window.calls.push('local')},'browser-test');
            }''')
            quick = page.locator('#browser-test [data-quick-custom] [data-custom-command="saveReport"]')
            assert await quick.is_visible()
            assert await quick.inner_text() == 'S'
            await quick.click()
            assert await page.evaluate('window.calls') == ['local']
            assert await page.evaluate('AzuntEditor.unregisterCommand("saveReport","browser-test")') is True
            assert await quick.count() == 0
            await page.locator('[data-azunt-menu="export"]').click()
            local = page.locator('#browser-test [data-menu-panel] [data-custom-command="saveReport"]')
            assert await local.inner_text() == 'Global Save'
            await local.click()
            assert await page.evaluate('window.calls') == ['local','global']
            assert await page.evaluate('AzuntEditor.unregisterCommand("saveReport","browser-test")') is False
            assert await page.evaluate('AzuntEditor.unregisterCommand("saveReport")') is True
            await page.locator('[data-azunt-menu="export"]').click()
            assert await page.locator('#browser-test [data-menu-panel] [data-custom-command="saveReport"]').count() == 0
            await page.keyboard.press('Escape')

            # Invalid placement fails before registry update.
            failed = await page.evaluate('''() => {
               try { AzuntEditor.registerCommand({id:'bad',label:'Bad',placement:'outside'}); return false; }
               catch(error) {return /placement/.test(error.message);}
            }''')
            assert failed
            assert await page.locator('[data-custom-command="bad"]').count() == 0

            await page.evaluate('''() => {
              window.blazorCalls = [];
              AzuntEditor.mount('browser-test', { invokeMethodAsync:(name,id) => {
                window.blazorCalls.push([name,id]); return Promise.resolve();
              }});
              AzuntEditor.setCommands('browser-test', [
                {id:'downloadPdf',label:'Download PDF',group:'Export',placement:'Toolbar',icon:'PDF'},
                {id:'editRecord',label:'Edit record',group:'Tools',placement:'Menu',requiresEdit:true}
              ]);
            }''')
            assert await page.locator('#browser-test [data-quick-custom] [data-custom-command="downloadPdf"]').is_visible()
            failed = await page.evaluate('''() => {
              try { AzuntEditor.setCommands('browser-test', [
                {id:'other',label:'Other'}, {id:'nope',label:'Invalid',placement:'error'}]); return false; }
              catch(e) { return /placement/.test(e.message); }
            }''')
            assert failed
            assert await page.locator('#browser-test [data-quick-custom] [data-custom-command="downloadPdf"]').is_visible()
            failed = await page.evaluate('''() => {
              try { AzuntEditor.setCommands('browser-test', [
                {id:'dup',label:'One'}, {id:'dup',label:'Two'}]); return false; }
              catch(e) { return /Duplicate/.test(e.message); }
            }''')
            assert failed
            assert await page.locator('#browser-test [data-quick-custom] [data-custom-command="downloadPdf"]').is_visible()

            # A replaced toolbar DOM must retain configured Blazor commands.
            await page.evaluate('''() => {
              const root=document.getElementById('browser-test');
              const old=root.querySelector('.az-azunt-editor-toolbar');
              old.replaceWith(old.cloneNode(true));
              AzuntEditor.mount('browser-test');
            }''')
            await page.locator('#browser-test [data-quick-custom] [data-custom-command="downloadPdf"]').click()
            assert ['OnCustomCommand','downloadPdf'] in await page.evaluate('window.blazorCalls')
            assert await page.locator('#browser-test [data-quick-custom] [data-custom-command="downloadPdf"]').count() == 1

            # Regression: Edit -> View -> Edit must not drop Blazor export commands.
            await page.evaluate('''() => {
              AzuntEditor.setMode('browser-test', 'View');
              AzuntEditor.setMode('browser-test', 'Edit');
            }''')
            await page.locator('[data-azunt-menu="export"]').click()
            pdf = page.locator('#browser-test [data-menu-panel] [data-custom-command="downloadPdf"]')
            assert await pdf.count() == 1, 'Blazor command lost after Edit/View/Edit'
            await pdf.click()
            assert (await page.evaluate('window.blazorCalls'))[-1:] == [['OnCustomCommand','downloadPdf']]
            await page.keyboard.press('Escape')

            # Read-only editors disable mutating host actions but permit exports.
            await page.evaluate('''() => {
              AzuntEditor.unmount('browser-test');
              document.getElementById('browser-test').dataset.readOnly='true';
              AzuntEditor.mount('browser-test', {invokeMethodAsync:(name,id)=>{
                window.blazorCalls.push([name,id]); return Promise.resolve();
              }});
              AzuntEditor.setCommands('browser-test', [
                {id:'editRecord',label:'Edit record',group:'Tools',requiresEdit:true,placement:'Toolbar'},
                {id:'downloadPdf',label:'Download PDF',group:'Export',placement:'Toolbar'}
              ]);
            }''')
            assert await page.locator('#browser-test [data-quick-custom] [data-custom-command="editRecord"]').is_disabled()
            assert await page.locator('#browser-test [data-quick-custom] [data-custom-command="downloadPdf"]').is_enabled()
            await page.locator('#browser-test [data-quick-custom] [data-custom-command="downloadPdf"]').click()
            assert (await page.evaluate('window.blazorCalls'))[-1:] == [['OnCustomCommand','downloadPdf']]
            assert not errors, errors
            print('PASS: command registration/removal, validation, atomic replace, remount, read-only')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
