"""Independent Chromium print-layout smoke test for 0.8.1 PDF color CSS.

This executes Chromium printing of the same stylesheet as the C# exporter,
not the .NET PDF generation service or its HTML colorization code.
"""
from pathlib import Path
from html import escape
import os
import re

from playwright.sync_api import sync_playwright
import fitz

root = Path(__file__).resolve().parents[1]
source = (root / 'src/Azunt.Editor.Export/Pdf/PdfDocumentExporter.cs').read_text()
match = re.search(r'<style>\s*(.*?)\s*</style>', source, re.S)
assert match, 'PDF stylesheet missing'
css = match.group(1)
for name in ('keyword', 'comment', 'string', 'number', 'tag'):
    assert f'.az-pdf-code-{name}' in css, f'Missing PDF color rule: {name}'

code = '\n'.join(f'// printed source line {n:03d}: ' + ('data ' * 24)
                 for n in range(1, 190))
html = (f'<html><head><meta charset="utf-8"><style>{css}</style></head><body>'
        '<h1>Azunt 0.8.1 PDF export</h1>'
        '<pre class="az-code-block" data-language="csharp"><code>'
        '<span class="az-pdf-code-keyword">public</span> '
        '<span class="az-pdf-code-keyword">class</span> Report {\n'
        '<span class="az-pdf-code-string">"안전한 코드"</span>\n'
        '<span class="az-pdf-code-comment">// Print syntax colors</span>\n'
        + escape(code) + '</code></pre><p>Printable document tail</p></body></html>')

with sync_playwright() as p:
    launch = {'headless': True, 'args': ['--no-sandbox', '--disable-dev-shm-usage']}
    exe = os.getenv('AZUNT_TEST_CHROMIUM')
    if not exe and Path('/usr/bin/chromium').exists(): exe = '/usr/bin/chromium'
    if exe: launch['executable_path'] = exe
    browser = p.chromium.launch(**launch)
    page = browser.new_page(java_script_enabled=False)
    page.set_content(html)
    page.emulate_media(media='print')
    assert page.locator('.az-pdf-code-keyword').first.evaluate(
        'el => getComputedStyle(el).color') == 'rgb(124, 58, 237)', 'Unexpected keyword color'
    pdf = page.pdf(format='A4', print_background=True,
                   margin={'top': '16mm', 'right': '16mm', 'bottom': '16mm', 'left': '16mm'})
    browser.close()

with fitz.open(stream=pdf, filetype='pdf') as document:
    pages = '\n'.join(page.get_text() for page in document)
    assert len(document) > 1, 'Expected multi-page code printout'
    for marker in ('Azunt 0.8.1', 'public class', 'printed source line 189', 'Printable document tail'):
        assert marker in pages, f'PDF text clipped: {marker}'
    print(f'PASS: print CSS colors and {len(document)} pages with code and document tail')
