"""Browser check of the actual PDF CSS template with preset values.
Does not execute the C# exporter; the .NET smoke tests cover that separately.
"""
from pathlib import Path
import re, os
import fitz
from playwright.sync_api import sync_playwright
root = Path(__file__).resolve().parents[1]
folder = root / 'src/Azunt.Editor.Export/Pdf'
base = re.search(r'<style>\s*(.*?)\s*</style>', (folder/'PdfDocumentExporter.cs').read_text(), re.S).group(1)
source = (folder/'PdfLayoutStyles.cs').read_text()
template = re.search(r'FormattableString.Invariant\(\$\$"""\s*(.*?)\s*"""\)', source, re.S).group(1)
extra = re.search(r'css \+= style.BrowserDefaults\s*\? "(.*?)"\s*:\s*"(.*?)";',source,re.S).groups()
# Expected presets are intentionally separate from the CSS template.
presets = {
    'Standard': (15,15,1.6,8,8,'#aeb8c6','#f1f5f9','#0f172a',.6),
    'BrowserDefault': (16,16,1.2,1,1,'#cbd2da','#f1f4f7','#000000',1),
    'CompactReport': (13,12,1.4,3,5,'#cbd2da','#f1f4f7','#202124',.45),
    'BusinessDocument': (14,13,1.5,6,8,'#b9c8d8','#e8eff6','#202124',.45),
    'Minimal': (14,13,1.45,4,6,'#e1e4e8','#fafafa','#202124',.45),
    'Custom': (12,11,1.25,2,4,'#123456','#aabbcc','#123456',.45),
}
rows = ''.join(f'<tr><td>R-{i:06d}</td><td>Accounting</td></tr>' for i in range(1,81))
html = '<h2>Report</h2><p>Plain text</p><p><span style="font-size:20px;color:#123456">Explicit</span></p><table style="width:65%;table-layout:fixed"><thead><tr><th>Number</th><th>Department</th></tr></thead><tbody>'+rows+'</tbody></table><p>End of report</p>'
with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=os.getenv('AZUNT_TEST_CHROMIUM','/usr/bin/chromium'),headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
    page = browser.new_page(java_script_enabled=False, viewport={'width':900,'height':1100})
    page.emulate_media(media='print')
    counts = {}
    for name, values in presets.items():
        font,table,line,v,h,border,header,color,paragraph = values
        native = name=='BrowserDefault'; standard=name=='Standard'
        cssfont = "'Times New Roman', serif" if native else 'Arial, sans-serif' if name=='Custom' else "system-ui, -apple-system, 'Segoe UI', Arial, sans-serif"
        replacements = {'style.CssFont':cssfont,'style.FontSize':font,'style.TableFontSize':table,'line':'normal' if native else line,'style.TextColor':color,'style.ParagraphMarginEm':paragraph,'width':'100%' if standard else 'auto','margin':'0' if native else '12px 0' if standard else '8px 0','collapse':'separate' if native else 'collapse','wrap':'anywhere' if standard else 'normal','style.CellPaddingVertical':v,'style.CellPaddingHorizontal':h,'border':'none' if native else '1px solid '+border,'header':'transparent' if native else header}
        css=re.sub(r'\{\{(.*?)\}\}',lambda m: str(replacements[m[1]]),template)+extra[0 if native else 1].replace('\\n','\n')
        page.set_content(f'<html><style>{base}</style><style>{css}</style><body>{html}</body></html>')
        def prop(selector, key): return page.locator(selector).first.evaluate('(e,k)=>getComputedStyle(e)[k]',key)
        assert prop('body','fontSize')==f'{font}px',name
        assert prop('td','fontSize')==f'{table}px',name
        assert prop('td','paddingTop')==f'{v}px' and prop('td','paddingLeft')==f'{h}px',name
        assert prop('span','fontSize')=='20px',name
        assert prop('td','borderTopWidth')==('0px' if native else '1px'),name
        assert abs(page.locator('table').evaluate('e=>e.getBoundingClientRect().width/e.parentElement.clientWidth')-.65)<.015,name
        pdf=page.pdf(format='A4', print_background=True,margin={k:'16mm' for k in ('top','bottom','left','right')})
        with fitz.open(stream=pdf,filetype='pdf') as result:
            text='\n'.join(pg.get_text() for pg in result)
            assert 'End of report' in text,name
            for i in range(1,81): assert text.count(f'R-{i:06d}')==1,(name,i)
            for pg in result:
                if re.search(r'R-\d{6}',pg.get_text()): assert 'Number' in pg.get_text(),name
            counts[name]=len(result)
    print('PASS: PDF CSS template, 6 styles, inline size, 65% width, 80 intact rows and repeating header:',counts)
    browser.close()
