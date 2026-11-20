"""Regression check: permanent Compact fullscreen button, Esc, Classic and Edit/View compatibility.
Run: python tests/editor-fullscreen-080-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT=Path(__file__).resolve().parents[1]
CS=(ROOT/'src/Azunt.Editor/Rendering/EditorMarkup.cs').read_text(encoding='utf-8')
CSS=(ROOT/'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS=(ROOT/'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
HTML=(ROOT/'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')

# Source contract: real host markup for MVC/Razor Pages and Blazor is shared.
assert 'data-quick-command=\\"fullscreen\\"' in CS
assert 'az-azunt-editor-quick-fullscreen' in CS and 'az-azunt-editor-quick-fullscreen' in CSS
assert 'Button("fullscreen",' in CS
assert 'button[data-command="fullscreen"],button[data-quick-command="fullscreen"]' in JS
assert 'or "fullscreen") w.Append' in CS

# Mirror the new Compact button; all other markup comes from the browser regression fixture.
FULLSCREEN_BUTTON='''<button type="button" class="az-azunt-editor-button az-azunt-editor-quick-fullscreen"
 data-quick-command="fullscreen" aria-pressed="false" aria-label="Enter fullscreen"
 title="Fullscreen (Esc to exit)"><span class="az-azunt-editor-button-icon" aria-hidden="true">⛶</span></button>'''
# Mirror the Compact group layout emitted by EditorMarkup (the older fixture uses flat buttons).
start=HTML.index('<div class="az-azunt-editor-quick-row"')
end=HTML.index('</div><div class="az-azunt-editor-classic-groups"', start)
quick='''<div class="az-azunt-editor-quick-row" data-compact-quick>
<span class="az-azunt-editor-quick-group"><span class="az-azunt-editor-quick-label">History</span><span class="az-azunt-editor-quick-controls"><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="undo">↶</button><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="redo">↷</button></span></span>
<span class="az-azunt-editor-quick-group"><span class="az-azunt-editor-quick-label">Text formatting</span><span class="az-azunt-editor-quick-controls"><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="bold">B</button><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="italic">I</button><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="underline">U</button></span></span>
<span class="az-azunt-editor-quick-group az-azunt-editor-quick-group-optional"><span class="az-azunt-editor-quick-label">Lists</span><span class="az-azunt-editor-quick-controls"><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="ul">•</button></span></span>
<span class="az-azunt-editor-quick-group az-azunt-editor-quick-group-optional"><span class="az-azunt-editor-quick-label">Insert</span><span class="az-azunt-editor-quick-controls"><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="link">Link</button></span></span>
<span class="az-azunt-editor-quick-group az-azunt-editor-quick-group-optional"><span class="az-azunt-editor-quick-label">Tools</span><span class="az-azunt-editor-quick-controls"><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="find">Find</button></span></span>
<span data-quick-custom class="az-azunt-editor-quick-custom"></span>
<button class="az-azunt-editor-menu-trigger az-azunt-editor-quick-more" data-azunt-menu="more">More ⋯</button>'''
HTML=HTML[:start]+quick+FULLSCREEN_BUTTON+'</div>'+HTML[end+len('</div>'):]
HTML=HTML.replace('''<button data-azunt-menu="more" type="button">More</button></div>''',
 '''<button class="az-azunt-editor-menu-trigger az-azunt-editor-quick-more" data-azunt-menu="more" type="button">More</button>'''+FULLSCREEN_BUTTON+'</div>')
HTML=HTML.replace('''<button data-command="fullscreen" type="button">Fullscreen</button>''',
 '''<button data-command="fullscreen" aria-pressed="false" type="button">Fullscreen</button>''')

async def main():
    async with async_playwright() as pw:
        browser=await pw.chromium.launch(executable_path='/usr/bin/chromium',headless=True,
                                          args=['--no-sandbox','--disable-dev-shm-usage'])
        try:
            for width,scheme in ((280,'light'),(390,'light'),(1180,'light'),(390,'dark')):
                ctx=await browser.new_context(viewport={'width':width,'height':680},color_scheme=scheme)
                page=await ctx.new_page()
                errors=[]
                page.on('pageerror',lambda e: errors.append(str(e)))
                await page.set_content(HTML)
                await page.add_style_tag(content=CSS)
                await page.add_script_tag(content=JS)
                root=page.locator('#browser-test')
                fullscreen=root.locator('[data-quick-command="fullscreen"]')
                classic=root.locator('[data-command="fullscreen"]')
                assert await fullscreen.is_visible(), (width,'hidden')
                rect=await fullscreen.bounding_box()
                assert rect and rect['x'] >= 0 and rect['x'] + rect['width'] <= width + 1, (width,rect)
                assert await fullscreen.get_attribute('aria-pressed') == 'false'
                initial=await page.evaluate('AzuntEditor.getHtml("browser-test")')
                await fullscreen.click()
                assert await root.evaluate('(el)=>el.classList.contains("az-azunt-editor-fullscreen")')
                assert await fullscreen.get_attribute('aria-pressed')=='true'
                assert await classic.get_attribute('aria-pressed')=='true'
                assert await fullscreen.get_attribute('aria-label')=='Exit fullscreen'
                bounds=await root.bounding_box()
                assert bounds and bounds['x']==0 and bounds['y']==0 and abs(bounds['width']-width)<=1, (width,bounds)
                assert await page.evaluate('AzuntEditor.getHtml("browser-test")')==initial
                await page.keyboard.press('Escape')
                assert not await root.evaluate('(el)=>el.classList.contains("az-azunt-editor-fullscreen")')
                assert await fullscreen.get_attribute('aria-pressed')=='false'
                assert await fullscreen.get_attribute('aria-label')=='Enter fullscreen'
                assert await fullscreen.evaluate('(el)=>document.activeElement===el'), 'Focus must return after Esc'
                # Using the button again must exit without changing saved document HTML.
                await fullscreen.click()
                await fullscreen.evaluate('(el)=>el.click()')
                assert not await root.evaluate('(el)=>el.classList.contains("az-azunt-editor-fullscreen")')
                assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == initial
                # Classic retains its original fullscreen command and shared pressed state.
                await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Classic")')
                assert await classic.is_visible()
                await classic.click()
                assert await root.evaluate('(el)=>el.classList.contains("az-azunt-editor-fullscreen")')
                assert await fullscreen.get_attribute('aria-pressed')=='true'
                await page.keyboard.press('Escape')
                assert not await root.evaluate('(el)=>el.classList.contains("az-azunt-editor-fullscreen")')
                # No fullscreen overlay may remain after Edit -> View, where
                # the editor deliberately has no keyboard/toolbar listeners.
                await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Compact")')
                await fullscreen.click()
                await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
                assert not await root.evaluate('(el)=>el.classList.contains("az-azunt-editor-fullscreen")')
                await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
                assert await fullscreen.get_attribute('aria-pressed')=='false'
                assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == initial
                assert not errors, (width,scheme,errors)
                await ctx.close()
            print('PASS: Compact/fullscreen button persistent 280/390/1180, dark mode, Escape, focus, HTML, Classic')
        finally:
            await browser.close()

if __name__=='__main__':
    asyncio.run(main())
