"""Exercise real editor controls under OS dark/light preferences.

Run: python tests/editor-dark-theme-browser.py
"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT=Path(__file__).resolve().parents[1]
HTML=(ROOT/'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')
CSS=(ROOT/'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
JS=(ROOT/'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')

async def colors(page, selector):
    return await page.locator(selector).first.evaluate('''el => {
      const s=getComputedStyle(el);
      return { foreground:s.color,background:s.backgroundColor, border:s.borderTopColor };
    }''')

async def main():
  async with async_playwright() as pw:
    browser=await pw.chromium.launch(executable_path='/usr/bin/chromium', headless=True,
                                      args=['--no-sandbox','--disable-dev-shm-usage'])
    for scheme in ('light','dark'):
      ctx=await browser.new_context(color_scheme=scheme,viewport={'width':1100,'height':750})
      page=await ctx.new_page()
      errors=[]
      page.on('pageerror',lambda e: errors.append(str(e)))
      await page.set_content(HTML)
      await page.add_style_tag(content=CSS)
      await page.add_script_tag(content=JS)
      await page.evaluate('AzuntEditor.mount("browser-test")')
      # Default form styling must never become light-on-light in dark mode.
      await page.locator('[data-azunt-menu="more"]').click()
      panel=await colors(page, '[data-menu-panel]')
      assert panel['background']==('rgb(30, 41, 59)' if scheme=='dark' else 'rgb(255, 255, 255)'), panel
      await page.keyboard.press('Escape')
      await page.locator('[data-command="find"]').first.evaluate('el => el.click()')
      for selector in ('[data-find-query]','[data-find-next]'):
          c=await colors(page,selector)
          if scheme=='dark':
              assert c['background']=='rgb(15, 23, 42)',(selector,c)
              assert c['foreground']=='rgb(241, 245, 249)',(selector,c)
      await page.locator('[data-find-close]').click()
      await page.locator('[data-command="special-chars"]').first.evaluate('el => el.click()')
      c=await colors(page,'[data-insert-character]')
      if scheme=='dark':
          assert c['background']=='rgb(15, 23, 42)',c
          assert c['foreground']=='rgb(241, 245, 249)',c
      await page.locator('[data-chars-close]').click()
      # Dynamically-created writing controls were a source of color conflicts.
      await page.evaluate('''() => {
          const root=document.querySelector('#browser-test');
          const area=document.createElement('div'); area.className='az-azunt-editor-date-panel';
          area.innerHTML='<button>Insert date</button><select><option>Today</option></select>';
          root.append(area);
          const sig=document.createElement('div'); sig.className='az-azunt-editor-signature-panel';
          sig.innerHTML='<button>Clear</button><select><option>Pen</option></select>';
          root.append(sig);
          const input=document.createElement('label'); input.className='az-azunt-editor-input-label';
          input.innerHTML='<input type="number" value="12">'; root.append(input);
      }''')
      if scheme=='dark':
          for selector in ('.az-azunt-editor-date-panel button','.az-azunt-editor-date-panel select',
                           '.az-azunt-editor-signature-panel button','.az-azunt-editor-signature-panel select',
                           '.az-azunt-editor-input-label input[type="number"]'):
              c=await colors(page,selector)
              assert c['background']=='rgb(15, 23, 42)',(selector,c)
              assert c['foreground']=='rgb(241, 245, 249)',(selector,c)
      await page.locator('.az-azunt-editor-canvas').fill('<p>Test</p>')
      assert not errors,(scheme,errors)
      await ctx.close()
    await browser.close()
  print('PASS: dark/light editor input, writing panel, popover and toolbar contrast')

if __name__ == '__main__': asyncio.run(main())
