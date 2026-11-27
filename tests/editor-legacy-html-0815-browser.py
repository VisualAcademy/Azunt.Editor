"""Synthetic fixtures based on legacy report markup, not production records."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text()
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text()
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
DOC = '''<p align="center"><span style="font-family:Verdana, Geneva, sans-serif;font-size:small;color:skyblue;font-weight:bold">Legacy report</span></p>
<p><font face="Arial" size="3" color="#123">Old font</font> <span style="font-size:14pt;font-style:italic;text-decoration:underline">Point size</span></p>
<ol type="I" start="4"><li>Fourth</li><li>Fifth</li></ol><ol style="list-style-type:lower-roman"><li>Lower</li></ol>
<table width="100%" style="border:0"><tbody><tr><td class="text-right" style="width:120px;border-style:none;vertical-align:bottom">Name</td><td style="width:47.4561%;border-style:none">Signature</td></tr><tr><td style="border:0;border-top:1px solid black">Line</td><td class="text-nowrap">Date</td></tr></tbody></table>
<p><img src="/images/demo.png" style="width:120px;height:60px" alt="Sample"></p>
<p style="position:fixed;background-image:url(https://invalid.example/test);font-size:999px" onclick="alert(1)">Safe text</p><script>throw Error('bad')</script><a href="javascript:alert(1)">Link</a>'''

async def main():
    async with async_playwright() as pw:
        options = {'headless': True}
        path = os.getenv('AZUNT_CHROMIUM_PATH') or ('/usr/bin/chromium' if Path('/usr/bin/chromium').exists() else None)
        if path: options['executable_path'] = path
        if os.name != 'nt': options['args'] = ['--no-sandbox','--disable-dev-shm-usage']
        browser = await pw.chromium.launch(**options)
        try:
            page = await browser.new_page()
            await page.route('**/*', lambda route: route.abort())
            await page.set_content(HTML)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            sanitized = await page.evaluate('html=>AzuntEditor.sanitizeHtml(html)',DOC)
            second = await page.evaluate('html=>AzuntEditor.sanitizeHtml(html)',sanitized)
            assert sanitized == second,(sanitized,second)
            await page.evaluate('html=>AzuntEditor.setHtml("browser-test", html)',DOC)
            canvas=page.locator('#browser-test .az-azunt-editor-canvas')
            actual=await page.evaluate('AzuntEditor.getHtml("browser-test")')
            assert 'Legacy report' in actual and 'start="4"' in actual and '<font' not in actual
            assert not any(x in actual for x in ['onclick','position:','background-image','javascript:','<script','999px'])
            async def computed(selector, prop):
                return await canvas.locator(selector).first.evaluate('(e,p)=>getComputedStyle(e)[p]',prop)
            assert await computed('p span','fontSize')=='13px'
            assert await computed('p span','fontFamily')=='Verdana'
            assert await computed('p span','fontWeight')=='700'
            assert await computed('ol','listStyleType')=='upper-roman'
            assert await computed('ol:nth-of-type(2)','listStyleType')=='lower-roman'
            assert await computed('td','textAlign')=='right'
            assert await computed('td','borderTopStyle')=='none'
            assert await computed('tr:nth-child(2) td','borderTopStyle')=='solid'
            assert await computed('tr:nth-child(2) td','borderLeftStyle')=='none'
            assert await canvas.locator('img').get_attribute('width')=='120'
            await page.evaluate('AzuntEditor.setMode("browser-test","View")')
            assert await computed('ol','listStyleType')=='upper-roman'
            await page.evaluate('AzuntEditor.setMode("browser-test","Edit")')
            assert actual==await page.evaluate('AzuntEditor.getHtml("browser-test")')
            print('PASS: legacy fonts, Roman numbering, signature borders, widths, image sizing, safe round-trip and Edit/View')
        finally:
            await browser.close()

if __name__=='__main__': asyncio.run(main())
