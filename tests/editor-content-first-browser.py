"""Content-first Tab order and keyboard toolbar access (no .NET host required).
Run: python tests/editor-content-first-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                          args=['--no-sandbox', '--disable-dev-shm-usage'])
        page = await browser.new_page(viewport={'width': 1200, 'height': 1000})
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        await page.set_content(HTML)
        await page.evaluate('''() => {
            const root = document.querySelector('#browser-test');
            root.insertAdjacentHTML('beforebegin', '<input id="title">');
            root.insertAdjacentHTML('afterend', '<input id="project-number">');
            const other = root.cloneNode(true);
            other.id = 'second-editor';
            document.body.append(other);
            other.insertAdjacentHTML('afterend', '<input id="after-second">');
        }''')
        await page.add_style_tag(content=CSS)
        await page.add_script_tag(content=JS)
        root = page.locator('#browser-test')
        body = root.locator('.az-azunt-editor-canvas')
        async def active(selector):
            assert await page.locator(selector).evaluate('(e) => e === document.activeElement'), selector
        async def ordinary_order():
            await page.locator('#title').focus()
            await page.keyboard.press('Tab')
            await active('#browser-test .az-azunt-editor-canvas')
            await page.keyboard.press('Tab')
            await active('#project-number')
            await page.keyboard.press('Shift+Tab')
            await active('#browser-test .az-azunt-editor-canvas')
            await page.keyboard.press('Shift+Tab')
            await active('#title')
        for mode, menu in [('Compact', True), ('Compact', False), ('Classic', False)]:
            await page.evaluate('([mode,menu]) => { AzuntEditor.setToolbarMode("browser-test", mode); AzuntEditor.setShowMenuBar("browser-test", menu); }', [mode, menu])
            await ordinary_order()
            await body.focus()
            await page.keyboard.press('Alt+F10')
            target = ('[data-compact-menu] [data-azunt-menu="file"]' if menu else
                      '[data-azunt-menu="more"]' if mode == 'Compact' else '[data-classic-groups] [data-command="bold"]')
            await active('#browser-test ' + target)
            if mode == 'Compact' and not menu:
                await page.keyboard.press('Shift+Tab')
            else:
                await page.keyboard.press('Tab')
            assert await root.locator('.az-azunt-editor-toolbar').evaluate('(e) => e.contains(document.activeElement)')
            await page.keyboard.press('Escape')
            await active('#browser-test .az-azunt-editor-canvas')
            await ordinary_order()
            print(f'PASS: {mode}, menu={menu}, forward/reverse Tab, toolbar and Escape')
        # Custom toolbar controls registered after mount must also be skipped.
        await page.evaluate('''() => AzuntEditor.registerCommand({id:'customTest',label:'Custom test',placement:'toolbar',execute:()=>{}}, 'browser-test')''')
        await ordinary_order()
        # Source mode uses the visible textarea as the normal Tab stop.
        await root.locator('[data-classic-groups] [data-command="source"]').click()
        await active('#browser-test [data-azunt-source]')
        await page.keyboard.press('Shift+Tab')
        await active('#title')
        await page.keyboard.press('Tab')
        await active('#browser-test [data-azunt-source]')
        await page.keyboard.press('Alt+F10')
        await page.keyboard.press('Escape')
        await active('#browser-test [data-azunt-source]')
        await root.locator('[data-classic-groups] [data-command="source"]').click()
        await ordinary_order()
        # One editor's keyboard navigation must not change the next editor's tab stops.
        await page.locator('#project-number').focus()
        await page.keyboard.press('Tab')
        await active('#second-editor .az-azunt-editor-canvas')
        await page.keyboard.press('Tab')
        await active('#after-second')
        await page.keyboard.press('Shift+Tab')
        await active('#second-editor .az-azunt-editor-canvas')
        await page.keyboard.press('Shift+Tab')
        await active('#project-number')
        await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
        await page.locator('#title').focus()
        await page.keyboard.press('Tab')
        await active('#project-number')
        await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
        await ordinary_order()
        assert not errors, errors
        print('PASS: custom commands, HTML source, multiple editors and View/Edit')
        await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
