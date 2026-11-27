"""Browser regression for native Print Preview (real shared JS; no .NET host)."""
from pathlib import Path
import os, re
from playwright.sync_api import sync_playwright
import fitz

root = Path(__file__).resolve().parents[1]
js = (root / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
css = (root / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
fixture = (root / 'tests/editor-adaptive-browser.html').read_text().replace('data-toolbar-mode="compact"', 'data-toolbar-mode="classic"')
fixture = fixture.replace('<option value="Letter">Letter</option>', '<option value="Letter">Letter</option><option value="Legal">Legal</option>')
with sync_playwright() as p:
    args = dict(headless=True, args=['--no-sandbox', '--disable-dev-shm-usage'])
    exe = os.getenv('AZUNT_TEST_CHROMIUM') or ('/usr/bin/chromium' if Path('/usr/bin/chromium').exists() else None)
    if exe: args['executable_path'] = exe
    browser = p.chromium.launch(**args)
    page = browser.new_page(viewport={'width':1280,'height':900})
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.set_content(fixture)
    page.add_style_tag(content=css)
    page.add_script_tag(content=js)
    page.evaluate('AzuntEditor.mount("browser-test")')
    rows = ''.join(f'<tr><td>R-{i:06}</td><td>Accounting</td></tr>' for i in range(1,81))
    html = '<p>Print test</p><table><thead><tr><th>Report number</th><th>Department</th></tr></thead><tbody>'+rows+'</tbody></table><p>End of report</p>'
    page.evaluate('(html)=>AzuntEditor.setHtml("browser-test",html)',html)
    before = page.evaluate('AzuntEditor.getHtml("browser-test")')
    page.locator('[data-select="paper"]').select_option('Legal')
    page.locator('[data-select="orientation"]').select_option('landscape')
    with page.expect_popup() as event:
        page.locator('[data-command="preview"]').click()
    popup = event.value
    popup.on('pageerror', lambda error: errors.append(str(error)))
    popup.wait_for_selector('#azunt-print-controls button:enabled')
    canvas = popup.locator('.az-azunt-print-page')
    assert abs(canvas.evaluate('e=>e.getBoundingClientRect().width') - 355.6*96/25.4) < 2
    assert popup.evaluate('window.opener === null')
    assert canvas.get_attribute('data-print-profile') == 'standard'
    counts = {}
    for profile, font, pad in [('standard',16,6),('compact',12,3),('comfortable',16,8)]:
        popup.locator('select[aria-label="Print layout"]').select_option(profile)
        cell = canvas.locator('td').first
        assert abs(cell.evaluate('e=>parseFloat(getComputedStyle(e).fontSize)')-font)<.1
        assert cell.evaluate('e=>parseFloat(getComputedStyle(e).paddingTop)')==pad
        pdf = popup.pdf(prefer_css_page_size=True, print_background=True)
        with fitz.open(stream=pdf,filetype='pdf') as doc:
            text = '\n'.join(pg.get_text() for pg in doc)
            assert abs(doc[0].rect.width-1008)<2 and abs(doc[0].rect.height-612)<2, doc[0].rect
            assert 'Print / Save as PDF' not in text and 'End of report' in text
            for i in range(1,81): assert text.count(f'R-{i:06}')==1,(profile,i)
            for pg in doc:
                if re.search(r'R-\d{6}', pg.get_text()): assert 'Report number' in pg.get_text()
            counts[profile]=len(doc)
    assert counts['compact']<=counts['standard']<=counts['comfortable'],counts
    assert page.evaluate('AzuntEditor.getHtml("browser-test")')==before
    popup.close()
    # A failed image does not silently trigger the automatic print dialog.
    page.evaluate('AzuntEditor.setHtml("browser-test", "<p>Image test</p><img src=\\"data:image/png;base64,AAAA\\">")')
    with page.expect_popup() as event:
        page.locator('[data-command="print"]').click()
    failed = event.value
    failed.wait_for_selector('#azunt-print-controls button:enabled')
    assert 'Some images could not load' in failed.locator('[role="status"]').inner_text()
    assert not errors,errors
    print('PASS: native Print Preview profiles, legal landscape geometry, repeated headers, 80 records, missing-image warning, unchanged editor:', counts)
    browser.close()
