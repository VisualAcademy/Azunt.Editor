"""Reproduce real toolbar table insertion and grip visibility/regression (0.8.0 hotfix).
Run: python tests/editor-table-grip-hotfix-080-browser.py
Requires playwright Python and Chromium.
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT=Path(__file__).resolve().parents[1]
HTML=ROOT/'tests/editor-adaptive-browser.html'
JS=ROOT/'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS=ROOT/'src/Azunt.Editor/wwwroot/css/azunt-editor.css'

async def main():
  async with async_playwright() as pw:
    browser=await pw.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
    try:
      # Both host-rendered grips (real MVC/Razor/Blazor) and JS fallback grips.
      for host_handles in (False, True):
        page=await browser.new_page(viewport={'width':1200,'height':900})
        errors=[]
        page.on('pageerror', lambda exc: errors.append(str(exc)))
        await page.set_content(HTML.read_text())
        if host_handles:
          await page.evaluate('''() => {
            const root=document.getElementById('browser-test');
            root.insertAdjacentHTML('beforeend',
              '<button type="button" class="az-azunt-editor-table-move-handle" data-table-move-handle hidden>✥</button>'+ 
              '<button type="button" class="az-azunt-editor-table-column-handle" data-table-column-handle hidden></button>'+ 
              '<div class="az-azunt-editor-table-drop-marker" data-table-drop-marker hidden></div>');
          }''')
        await page.add_style_tag(content=CSS.read_text())
        await page.add_script_tag(content=JS.read_text())
        root=page.locator('#browser-test')
        canvas=root.locator('.az-azunt-editor-canvas')
        move=root.locator('[data-table-move-handle]')
        resize=root.locator('[data-table-column-handle]')
        await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Before <strong>ABCD</strong> after</p><p>Destination</p>")')
        # Toolbar insert while selection is inside nested inline formatting.
        await page.evaluate('''() => { const n=document.querySelector('#browser-test .az-azunt-editor-canvas strong').firstChild;
          const r=document.createRange();r.setStart(n,2);r.collapse(true);
          const s=getSelection();s.removeAllRanges();s.addRange(r); }''')
        await root.locator('[data-compact-quick] [data-quick-command="table"]').click()
        assert await canvas.locator('table').count()==1
        assert await canvas.locator('table').evaluate('(t)=>t.parentElement.classList.contains("az-azunt-editor-canvas")'), 'new table must be a root block'
        assert await move.is_visible() and await resize.is_visible(), 'grips missing after toolbar insertion'
        html=await page.evaluate('AzuntEditor.getHtml("browser-test")')
        assert html.index('<p>Before') < html.index('<table') < html.index('Destination')
        assert 'AB' in html and 'CD' in html and 'after' in html and 'Destination' in html, html
        print('PASS:', 'host' if host_handles else 'fallback', 'toolbar insertion splits paragraph without losing text; both grips visible')

        # Last-column selection should expose the last INTERNAL separator.
        await canvas.locator('table tr:first-child td').last.click()
        assert await move.is_visible() and await resize.is_visible(), 'last column hides grips'
        box=await resize.bounding_box()
        assert box and box['height']>20
        widths_before=await canvas.locator('table tr:first-child td').evaluate_all('(cells)=>cells.map(c=>c.getBoundingClientRect().width)')
        await page.mouse.move(box['x']+box['width']/2,box['y']+20)
        await page.mouse.down()
        await page.mouse.move(box['x']-30,box['y']+20,steps=6)
        await page.mouse.up()
        widths_after=await canvas.locator('table tr:first-child td').evaluate_all('(cells)=>cells.map(c=>c.getBoundingClientRect().width)')
        assert abs(widths_after[0]-widths_before[0])>10
        assert 'width:' in await page.evaluate('AzuntEditor.getHtml("browser-test")')
        print('PASS:', 'host' if host_handles else 'fallback', 'last column selection exposes resize and persists widths')

        # Existing HTML may legitimately wrap table in a div. It still needs grips.
        await page.evaluate('AzuntEditor.setHtml("browser-test", "<p>One</p><div><p>Caption</p><table><tr><td>A</td><td>B</td></tr></table></div><p>Two</p>")')
        await canvas.locator('table td').last.click()
        assert await move.is_visible() and await resize.is_visible(), 'wrapped table hides grips'
        # Keyboard move must use root-level sibling to avoid NotFoundError.
        await move.focus()
        await page.keyboard.press('Alt+ArrowDown')
        assert await canvas.locator('table').evaluate('(t)=>t.parentElement.classList.contains("az-azunt-editor-canvas")')
        assert (await page.evaluate('AzuntEditor.getHtml("browser-test")')).find('Caption')>=0
        print('PASS:', 'host' if host_handles else 'fallback', 'wrapped table can be moved without losing caption')

        # Nested tables within cells remain excluded from whole-table control.
        await page.evaluate('AzuntEditor.setHtml("browser-test", "<table><tr><td>Outer<table><tr><td>Inner</td><td>Two</td></tr></table></td><td>Right</td></tr></table>")')
        await canvas.locator('table table td').first.click()
        assert not await move.is_visible() and not await resize.is_visible(), 'nested-table movement should remain guarded'
        print('PASS:', 'host' if host_handles else 'fallback', 'nested table remains guarded')
        assert not errors, errors
        await page.close()
    finally:
      await browser.close()

if __name__=='__main__':
  asyncio.run(main())
