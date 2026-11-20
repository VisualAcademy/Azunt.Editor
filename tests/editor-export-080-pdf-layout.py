"""0.8.0 print-layout regression (developer-only): pip install playwright pymupdf.

Tests the actual print CSS embedded in PdfDocumentExporter.cs with headless Chromium.
This does NOT run the .NET export service or assert Playwright browser availability
on Windows ARM64 / Azure.
"""
from pathlib import Path
import os
import re
from playwright.sync_api import sync_playwright
import fitz

project = Path(__file__).resolve().parents[1]
pdf_source = (project / 'src/Azunt.Editor.Export/Pdf/PdfDocumentExporter.cs').read_text()
css_match = re.search(r'<style>\s*(.*?)\s*</style>', pdf_source, re.S)
assert css_match, 'PDF print stylesheet missing'
css = css_match.group(1)
assert 'print-color-adjust:exact' in css
assert 'break-inside:auto' in css
code = '\n'.join(f'// numbered source line {i:03d} with the literal <safe> marker and a long tail of padding text' for i in range(1, 241))
from html import escape
html = (f'<html><head><meta charset="utf-8"><style>{css}</style></head><body>'
        '<h1>Export regression report</h1>'
        '<table><thead><tr><th>Asset</th><th>Status</th></tr></thead><tbody>'
        + ''.join(f'<tr><td>EGM-{n:03d}</td><td>Active</td></tr>' for n in range(40))
        + '</tbody></table><hr data-azunt-page-break="true">'
        + f'<pre class="az-code-block"><code>{escape(code)}</code></pre>'
        + '<p>Document completion marker</p></body></html>')
with sync_playwright() as p:
    executable = os.environ.get('AZUNT_TEST_CHROMIUM')
    if not executable and Path('/usr/bin/chromium').exists():
        executable = '/usr/bin/chromium'
    launch = dict(headless=True, args=['--no-sandbox', '--disable-dev-shm-usage'])
    if executable: launch['executable_path'] = executable
    browser = p.chromium.launch(**launch)
    page = browser.new_page(java_script_enabled=False)
    page.set_content(html)
    page.emulate_media(media='print')
    pdf = page.pdf(format='A4', print_background=True,
                   margin={'top':'16mm','right':'16mm','bottom':'16mm','left':'16mm'})
    browser.close()
with fitz.open(stream=pdf, filetype='pdf') as doc:
    text = '\n'.join(page.get_text() for page in doc)
    assert len(doc) > 2, f'expected multipage layout, found {len(doc)} page(s)'
    assert 'EGM-039' in text and '240' in text, 'table or trailing code line lost'
    assert 'Document completion marker' in text, 'document tail clipped'
    print(f'PASS: PDF stylesheet rendered {len(doc)} pages with table, code and final marker')
