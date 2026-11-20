"""0.7.2 code block UX tests: python tests/editor-code-blocks-072-browser.py"""
import asyncio
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
FIXTURE = (ROOT / 'tests/editor-adaptive-browser.html').read_text()
PANEL = '''<div class="az-code-panel" data-code-panel hidden role="group" aria-label="Code block">
<label>Language <select data-code-language><option value="csharp">C#</option><option value="javascript">JavaScript</option><option value="sql">SQL</option><option value="html">HTML</option><option value="css">CSS</option></select></label>
<label>Code <textarea data-code-text maxlength="100000"></textarea></label>
<button data-code-apply>Apply code</button><button data-code-cancel>Cancel</button></div>'''

async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path='/usr/bin/chromium', headless=True, args=['--no-sandbox', '--disable-dev-shm-usage'])
        try:
            page = await browser.new_page(viewport={'width': 620, 'height': 700})
            failures = []
            page.on('pageerror', lambda e: failures.append(str(e)))
            html = FIXTURE.replace('<div class="az-azunt-editor-canvas"', PANEL + '<div class="az-azunt-editor-canvas"')
            await page.set_content(html)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            await page.evaluate('AzuntEditor.mount("browser-test")')
            canvas = page.locator('#browser-test .az-azunt-editor-canvas')
            panel = page.locator('#browser-test [data-code-panel]')
            textarea = panel.locator('textarea')
            code = canvas.locator('pre.az-code-block > code')

            multiline = 'public class Demo {\n    Console.WriteLine("hi");\n\n    // line four\n}'
            await page.evaluate('([t]) => AzuntEditor.setHtml("browser-test", `<pre data-language="csharp"><code>${t.replace(/&/g,"&amp;").replace(/</g,"&lt;")}</code></pre>`)', [multiline])
            gutter = canvas.locator('.az-code-line-numbers')
            assert await gutter.count() == 1
            assert await gutter.text_content() == '1\n2\n3\n4\n5'
            assert await gutter.get_attribute('aria-hidden') == 'true'
            assert await code.text_content() == multiline
            saved = await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert 'az-code-line-numbers' not in saved and '1\n2\n3' not in saved
            assert await canvas.locator('.az-code-token-type').count() >= 2
            print('PASS: line numbers rendered and accessible without polluting HTML, code, or Copy')

            long_line = 'const note = "' + ('x'*1800) + '";\n// end'
            await page.evaluate('([t]) => AzuntEditor.setHtml("browser-test", `<pre data-language="javascript"><code>${t}</code></pre>`)', [long_line])
            metrics = await page.evaluate('''() => { const pre=document.querySelector('#browser-test pre.az-code-block');
              const code=pre.querySelector('code'); const gutter=pre.querySelector('.az-code-line-numbers');
              pre.scrollLeft=800;
              return {client:pre.clientWidth, scroll:pre.scrollWidth, gutterOffset:gutter.getBoundingClientRect().left-pre.getBoundingClientRect().left,
                codeText:code.textContent, wrap:getComputedStyle(code).whiteSpace};
            }''')
            assert metrics['scroll'] > metrics['client'] and metrics['wrap']=='pre', metrics
            assert metrics['gutterOffset'] >= 0 and metrics['gutterOffset'] < 15, metrics
            assert metrics['codeText'] == long_line
            print('PASS: long lines scroll horizontally while line number gutter remains visible')

            # Language-specific lexing is presentation-only and preserves literal source.
            cases = [
                ('html', '<a class="btn" href="https://example.test">A&amp;B</a>', ['az-code-token-tag', 'az-code-token-property', 'az-code-token-string']),
                ('css', '.box { color: #aabbcc; --theme: 12px; } /* styling */', ['az-code-token-property', 'az-code-token-number', 'az-code-token-comment']),
                ('sql', "SELECT 'FROM users' AS name -- comment\nFROM members WHERE id=42", ['az-code-token-keyword', 'az-code-token-string', 'az-code-token-comment']),
                ('javascript', 'const url = "https://example.test"; // comment', ['az-code-token-keyword', 'az-code-token-string', 'az-code-token-comment']),
                ('csharp', 'public class Example { string s = @"say ""yes"""; }', ['az-code-token-type', 'az-code-token-keyword', 'az-code-token-string'])
            ]
            for lang, source, classes in cases:
                await page.evaluate('([lang,t]) => { const div=document.createElement("div");const pre=document.createElement("pre");pre.dataset.language=lang; const code=document.createElement("code");code.textContent=t;pre.appendChild(code); div.appendChild(pre);AzuntEditor.setHtml("browser-test",div.innerHTML); }', [lang, source])
                assert await code.text_content() == source
                for cls in classes:
                    assert await code.locator('.'+cls).count() > 0, (lang, cls, await code.inner_html())
                assert 'az-code-token' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            print('PASS: C#, JavaScript, SQL, HTML, CSS tokenize safely without modifying source')

            # Edit code text via real keyboard shortcuts.
            await canvas.locator('pre button[data-code-action="edit"]').click()
            assert await panel.is_visible()
            await textarea.fill('if (true) {')
            await textarea.press('End')
            await textarea.press('Enter')
            assert await textarea.input_value() == 'if (true) {\n    '
            await textarea.type('work();')
            await textarea.press('Enter')
            assert (await textarea.input_value()).endswith('    work();\n    ')
            await textarea.press('Shift+Tab')
            assert (await textarea.input_value()).endswith('    work();\n')
            await textarea.press('Tab')
            assert (await textarea.input_value()).endswith('    work();\n    ')
            print('PASS: Enter inherits indentation and Tab / Shift+Tab indent and outdent')

            await textarea.fill('alpha\nbeta\ngamma')
            await textarea.evaluate('(t) => t.setSelectionRange(0, t.value.length)')
            await textarea.press('Tab')
            assert await textarea.input_value() == '    alpha\n    beta\n    gamma'
            await textarea.press('Shift+Tab')
            assert await textarea.input_value() == 'alpha\nbeta\ngamma'
            await panel.locator('[data-code-apply]').click()
            assert await code.text_content() == 'alpha\nbeta\ngamma'
            await page.evaluate('AzuntEditor.setMode("browser-test", "View")')
            assert await canvas.locator('.az-code-line-numbers').count() == 1
            assert await canvas.locator('button[data-code-action="edit"]').count() == 0
            assert 'az-code-line-numbers' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            await page.evaluate('AzuntEditor.setMode("browser-test", "Edit")')
            assert await code.text_content() == 'alpha\nbeta\ngamma'
            print('PASS: multiline indent, editing, View roundtrip, and clean persistence')

            # 5,001-line documents must not create enormous DOM gutters.
            huge = '\n'.join('x' for _ in range(5001))
            await page.evaluate('([t]) => {const el=document.createElement("pre");el.dataset.language="sql";const c=document.createElement("code");c.textContent=t;el.appendChild(c);AzuntEditor.setHtml("browser-test",el.outerHTML);}', [huge])
            assert await gutter.count() == 0
            assert await code.text_content() == huge
            assert await canvas.locator('pre').get_attribute('data-code-lines-hidden') == 'true'
            assert 'code-lines-hidden' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert not failures, failures
            print('PASS: huge code blocks avoid expensive gutter DOM and remain lossless')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
