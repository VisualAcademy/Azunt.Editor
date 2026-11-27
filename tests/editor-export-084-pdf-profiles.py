"""Browser-only regression: actual CSS constants, synthetic semantic table.
Does not execute .NET, sanitizer or automatic header promotion. Optional
AZUNT_TEST_PDF_OUTPUT points to actual .NET smoke-test PDFs for validation.
Requires Python playwright + pymupdf and a local Chromium installation.
"""
from pathlib import Path
import os
import re
import fitz
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parents[1]
pdf_path = root / 'src/Azunt.Editor.Export/Pdf/PdfDocumentExporter.cs'
base = re.search(r'<style>\s*(.*?)\s*</style>', pdf_path.read_text(), re.S).group(1)
styles = (pdf_path.parent / 'PdfLayoutStyles.cs').read_text()
profiles = {'Standard': ''}
for name in ('Compact', 'Comfortable'):
    profiles[name] = re.search(rf'const string {name} = """\s*(.*?)\s*""";', styles, re.S).group(1)
rows = ''.join(f'<tr><td>R-{i:06d}</td><td>Accounting</td></tr>' for i in range(1, 81))
content = ('<p>Body size marker</p><p><span style="font-size:20px">Explicit size marker</span></p>'
           '<table><thead><tr><th>Report number</th><th>Department</th></tr></thead><tbody>' + rows +
           '</tbody></table><p>Document end</p>')

def verify(pdf, name):
    with fitz.open(stream=pdf, filetype='pdf') as doc:
        all_text = '\n'.join(p.get_text() for p in doc)
        for i in range(1, 81):
            assert all_text.count(f'R-{i:06d}') == 1, (name, i, 'missing/duplicated record')
        assert 'Document end' in all_text
        table_pages = [p for p in doc if re.search(r'R-\d{6}', p.get_text())]
        assert len(table_pages) >= 2
        for p in table_pages:
            assert 'Report number' in p.get_text(), (name, p.number, 'missing repeated header')
        return len(doc)

with sync_playwright() as p:
    args = dict(headless=True, args=['--no-sandbox', '--disable-dev-shm-usage'])
    exe = os.getenv('AZUNT_TEST_CHROMIUM') or ('/usr/bin/chromium' if Path('/usr/bin/chromium').exists() else None)
    if exe: args['executable_path'] = exe
    browser = p.chromium.launch(**args)
    page = browser.new_page(java_script_enabled=False)
    page.emulate_media(media='print')
    counts = {}
    for name, css in profiles.items():
        page.set_content(f'<html><style>{base}</style><style>{css}</style><body>{content}</body></html>')
        def size(selector):
            return float(page.locator(selector).first.evaluate('el => parseFloat(getComputedStyle(el).fontSize)'))
        expected = {'Standard': (12,12,6), 'Compact': (10,9,3), 'Comfortable': (14,12,8)}[name]
        assert abs(size('body') - expected[0]*4/3) < .01
        assert abs(size('td') - expected[1]*4/3) < .01
        assert size('span') == 20
        assert float(page.locator('td').first.evaluate('el => parseFloat(getComputedStyle(el).paddingTop)')) == expected[2]
        pdf = page.pdf(format='Letter', print_background=True, margin={k:'16mm' for k in ('top','bottom','left','right')},
                       display_header_footer=True,
                       footer_template='<div style="font-size:9px;width:100%;text-align:center"><span class="pageNumber"></span> / <span class="totalPages"></span></div>')
        counts[name] = verify(pdf, name)
        with fitz.open(stream=pdf, filetype='pdf') as doc:
            for i, pg in enumerate(doc, 1):
                assert f'{i} / {len(doc)}' in pg.get_text(), (name, 'page footer')
    assert counts['Compact'] < counts['Standard'] <= counts['Comfortable'], counts
    print('PASS: actual print CSS, 80 intact rows, semantic header repeat, footer, explicit font, page counts:', counts)
    browser.close()

if directory := os.getenv('AZUNT_TEST_PDF_OUTPUT'):
    counts = {name: verify((Path(directory) / f'{name}.pdf').read_bytes(), name) for name in profiles}
    assert counts['Compact'] < counts['Standard'] <= counts['Comfortable'], counts
    print('PASS: actual .NET PDFs (including automatic header promotion):', counts)
