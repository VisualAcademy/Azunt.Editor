"""0.7.1 programming code block regression. Run: python tests/editor-code-blocks-071-browser.py"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = (ROOT/'tests/editor-adaptive-browser.html').read_text()
CSS = (ROOT/'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
JS = (ROOT/'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
CODE_PANEL = '''<div class="az-code-panel" data-code-panel hidden role="group" aria-label="Insert or edit code block">
<label>Language <select data-code-language><option value="csharp">C#</option><option value="javascript">JavaScript</option><option value="sql">SQL</option><option value="html">HTML</option><option value="css">CSS</option></select></label>
<label>Code <textarea data-code-text maxlength="100000"></textarea></label><button data-code-apply>Apply code</button><button data-code-cancel>Cancel</button></div>'''

async def main():
  async with async_playwright() as pw:
    browser = await pw.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
    try:
      page = await browser.new_page(viewport={'width':1100,'height':850})
      errors=[]
      page.on('pageerror', lambda e: errors.append(str(e)))
      html = FIXTURE.replace('<div class="az-azunt-editor-canvas"', CODE_PANEL+'<div class="az-azunt-editor-canvas"')
      html = html.replace('</div>\n<div class="az-azunt-editor-canvas"', '</div>\n<div class="az-azunt-editor-canvas"')
      # Add code triggers to an existing toolbar while leaving all baseline controls unchanged.
      html = html.replace('<div class="az-azunt-editor-canvas"', '<div class="az-code-toolbar"><button data-quick-command="code-block" type="button">Code</button><button data-azunt-menu="codepicker" type="button">▾</button></div><div class="az-azunt-editor-canvas"')
      await page.set_content(html)
      await page.add_style_tag(content=CSS)
      await page.add_script_tag(content=JS)
      root=page.locator('#browser-test')
      canvas=root.locator('.az-azunt-editor-canvas')
      panel=root.locator('[data-code-panel]')
      # The fixture's extra code trigger must be in the toolbar to receive delegated clicks.
      await page.evaluate('''() => { const el=document.querySelector('#browser-test .az-code-toolbar');
        document.querySelector('#browser-test .az-azunt-editor-toolbar').appendChild(el);
      }''')
      await page.evaluate('AzuntEditor.mount("browser-test")')
      await root.locator('[data-quick-command="code-block"]').click()
      assert await panel.is_visible()
      sample = 'public class Demo {\n    string value = "<script>alert(1)</script>";\n    // harmless comment\n}'
      await panel.locator('textarea').fill(sample)
      await panel.locator('[data-code-apply]').click()
      pre=canvas.locator('pre[data-language="csharp"]')
      assert await pre.count() == 1
      assert await pre.locator('.az-code-token-keyword').count() >= 2
      assert await pre.locator('button[data-code-action="copy"]').count() == 1
      saved=await page.evaluate('AzuntEditor.getHtml("browser-test")')
      assert 'data-language="csharp"' in saved and '&lt;script&gt;' in saved and '<script>' not in saved, saved
      assert 'az-code-actions' not in saved and 'az-code-token' not in saved, saved
      assert await pre.locator('code').inner_text() == sample
      print('PASS: insert, color, and persist escaped C# source without UI markup')
      await pre.locator('button[data-code-action="edit"]').click()
      assert await panel.is_visible()
      assert await panel.locator('textarea').input_value() == sample
      await panel.locator('select').select_option('sql')
      await panel.locator('textarea').fill('SELECT id, name FROM users WHERE id = 42;')
      await panel.locator('[data-code-apply]').click()
      saved=await page.evaluate('AzuntEditor.getHtml("browser-test")')
      assert 'data-language="sql"' in saved and 'SELECT id, name FROM users WHERE id = 42;' in saved
      pre=canvas.locator('pre[data-language="sql"]')
      assert await pre.locator('.az-code-token-keyword').count() >= 3
      print('PASS: edit existing source, change language, and recolor')
      await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
      assert await pre.locator('[data-code-action="edit"]').count() == 0
      assert await pre.locator('[data-code-action="copy"]').count() == 1
      assert await canvas.get_attribute('contenteditable') == 'false'
      assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == saved
      await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
      assert await page.evaluate('AzuntEditor.getHtml("browser-test")') == saved
      print('PASS: Edit / View / Edit retains HTML and View has copy but no Edit')
      await page.evaluate('''() => { const root=document.querySelector('#browser-test');
        const editor=root.querySelector('.az-azunt-editor-canvas');
        editor.focus(); editor.dispatchEvent(new KeyboardEvent('keydown',{key:'z',ctrlKey:true,bubbles:true,cancelable:true}));
      }''')
      restored = await page.evaluate('AzuntEditor.getHtml("browser-test")')
      assert 'data-language="csharp"' in restored, restored
      print('PASS: code edit is an Undo unit')
      for language, source in [
        ('javascript','const x = "<img onerror=alert(1)>"; // JS'),
        ('html','<img src=x onerror="alert(1)">'),
        ('css','p { color: #aabbcc; }')]:
        await page.evaluate('''([lang, text]) => AzuntEditor.setHtml('browser-test',
          `<pre data-language="${lang}"><code>${text.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')}</code></pre>`)
        ''',[language,source])
        assert await canvas.locator('pre[data-language="'+language+'"]').count()==1
        assert await canvas.locator('pre code').inner_text()==source
        assert not await canvas.locator('[onerror]').count()
        assert 'az-code-actions' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
      print('PASS: JavaScript, HTML, CSS roundtrip as harmless text')
      await page.evaluate('''() => AzuntEditor.setHtml('browser-test',
        '<pre data-language="evil" onclick="alert(1)"><code>Not trusted</code></pre><script>alert(1)</script>')''')
      saved=await page.evaluate('AzuntEditor.getHtml("browser-test")')
      assert 'onclick' not in saved and '<script>' not in saved and 'data-language' not in saved
      print('PASS: unsupported language, event handler, and script rejected')
      assert not errors,errors
    finally:
      await browser.close()

if __name__=='__main__':
  asyncio.run(main())
