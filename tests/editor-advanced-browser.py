"""Azunt.Editor 0.5.1 advanced editing browser integration test.
Run: python tests/editor-advanced-browser.py (Python Playwright + Chromium).
"""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js'
CSS = ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css'
BASE = ROOT / 'tests/editor-features-browser.html'

async def main():
    async with async_playwright() as pw:
        launch = {'headless': True}
        if os.getenv('AZUNT_CHROMIUM_PATH'):
            launch['executable_path'] = os.environ['AZUNT_CHROMIUM_PATH']
        elif Path('/usr/bin/chromium').exists():
            launch['executable_path'] = '/usr/bin/chromium'
        if os.name != 'nt':
            launch['args'] = ['--no-sandbox','--disable-dev-shm-usage']
        browser = await pw.chromium.launch(**launch)
        page = await browser.new_page()
        try:
            original = BASE.read_text()
            original = original.replace('<script src="../src/Azunt.Editor/wwwroot/js/azunt-editor.js"></script>', '')
            original = original.replace('  </div>\n  <div class="az-azunt-editor-link-panel"', '''    <button type="button" data-command="table-merge-right">Merge</button>
    <button type="button" data-command="table-split">Split</button>
    <button type="button" data-command="table-row-before">Row before</button>
    <button type="button" data-command="table-col-before">Col before</button>
    <button type="button" data-command="table-header-toggle">Header</button>
    <button type="button" data-command="signature">Signature</button>
    <button type="button" data-command="datetime">Date/time</button>
    <button type="button" data-command="templates">Templates</button>
    <input type="color" data-select="tableBorderColor" value="#aeb8c6">
    <select data-select="tableBorderWidth"><option value="">Border px</option><option value="2">2 px</option></select>
  </div>
  <div class="az-azunt-editor-link-panel"''')
            original = original.replace('  <div class="az-azunt-editor-canvas"', '''  <div class="az-azunt-editor-signature-panel" data-signature-panel hidden>
    <canvas data-signature-canvas width="640" height="180" style="width:640px;height:180px"></canvas>
    <input type="color" data-signature-color value="#1e293b">
    <select data-signature-width><option value="2">Fine</option><option value="3" selected>Normal</option><option value="5">Thick</option></select>
    <button data-signature-clear>Clear</button>
    <button data-signature-insert>Insert signature</button>
    <button data-signature-close>Cancel</button>
  </div>
  <div class="az-azunt-editor-template-panel" data-template-panel hidden>
    <select data-template-list><option value="blank">Blank</option></select>
    <button data-template-apply>Load</button><button data-template-close>Cancel</button>
  </div>
  <div class="az-azunt-editor-date-panel" data-date-panel hidden>
    <button data-date-format="date">Date</button>
    <button data-date-format="time">Time</button>
    <button data-date-format="datetime">Date &amp; time</button>
    <button data-date-close>Cancel</button>
  </div>
  <div class="az-azunt-editor-canvas"''')
            await page.set_content(original)
            await page.add_style_tag(content=CSS.read_text())
            await page.add_script_tag(content=JS.read_text())
            async def load(html):
                await page.evaluate('(v) => AzuntEditor.setHtml("browser-test", v)', html)
            async def html():
                return await page.evaluate('AzuntEditor.getHtml("browser-test")')
            async def cmd(name):
                await page.locator(f'[data-command="{name}"]').click()
            await load('<table><tbody><tr><td>first</td><td>second</td><td>third</td></tr><tr><td>A</td><td>B</td><td>C</td></tr></tbody></table>')
            await page.locator('.az-azunt-editor-canvas tr').first.locator('td').first.click()
            await cmd('table-merge-right')
            result = await html()
            assert 'colspan="2"' in result and 'first' in result and 'second' in result, result
            await page.locator('[data-select="tableBorderColor"]').fill('#ff0000')
            # change event sent by fill for color input
            await page.locator('[data-select="tableBorderWidth"]').select_option('2')
            result = await html()
            assert 'border-color: rgb(255, 0, 0)' in result or 'border-color: #ff0000' in result, result
            assert 'border-width: 2px' in result, result
            await load(result)
            assert 'colspan="2"' in await html(), 'Merged cell should survive save and restore'
            await page.locator('.az-azunt-editor-canvas tr').first.locator('td').first.click()
            await cmd('table-split')
            result = await html()
            assert 'colspan' not in result and len(await page.locator('.az-azunt-editor-canvas tr').first.locator('td').all()) == 3, result

            # Directional insertion and semantic header toggle keep the table rectangular.
            await page.locator('.az-azunt-editor-canvas tr').nth(1).locator('td').nth(1).click()
            await cmd('table-row-before')
            assert await page.locator('.az-azunt-editor-canvas tr').count() == 3
            await cmd('table-col-before')
            assert await page.locator('.az-azunt-editor-canvas tr').first.locator('td').count() == 4
            await cmd('table-header-toggle')
            assert await page.locator('.az-azunt-editor-canvas tr').nth(1).locator('th').count() == 4
            assert '<th' in await html()

            # Native browser pointer event path for signature drawing and PNG insertion.
            await load('<p>Approved: </p>')
            await cmd('signature')
            await page.locator('[data-signature-color]').fill('#cc2200')
            await page.locator('[data-signature-width]').select_option('5')
            canvas = page.locator('[data-signature-canvas]')
            await canvas.scroll_into_view_if_needed()
            rect = await canvas.bounding_box()
            await page.mouse.move(rect['x']+30,rect['y']+60)
            await page.mouse.down()
            await page.mouse.move(rect['x']+130,rect['y']+40,steps=7)
            await page.mouse.up()
            await page.locator('[data-signature-insert]').click()
            result = await html()
            assert '<img' in result and 'alt="Signature"' in result and 'data:image/png;base64,' in result, result[:600]
            assert await page.locator('.az-azunt-editor-canvas img').count() == 1
            # The inserted image should be cropped to the ink bounds, not full 640x180.
            dimensions = await page.locator('.az-azunt-editor-canvas img').first.evaluate('(img) => ({w:img.naturalWidth,h:img.naturalHeight})')
            assert 110 <= dimensions['w'] < 180 and 20 <= dimensions['h'] < 90, dimensions
            assert 'width=' not in result[result.index('<img'):result.index('>', result.index('<img'))]
            await load(result)
            assert 'alt="Signature"' in await html(), 'Signature should survive save/restore'

            await load('<p>Report: </p>')
            await cmd('datetime')
            await page.locator('[data-date-format="date"]').click()
            assert (await page.locator('.az-azunt-editor-canvas').inner_text()).strip() != 'Report:', 'Date inserted'

            # Require explicit confirmation before overwriting nonempty reports.
            await load('<p>Important current draft</p>')
            await cmd('templates')
            await page.locator('[data-template-list]').select_option('meeting')
            page.once('dialog', lambda d: d.dismiss())
            await page.locator('[data-template-apply]').click()
            assert 'Important current draft' in await html(), 'Cancelled replacement must retain draft'
            page.once('dialog', lambda d: d.accept())
            await page.locator('[data-template-apply]').click()
            assert 'Meeting notes' in await html() and '<table>' in await html(), 'Template loaded'
            await page.evaluate('AzuntEditor.registerTemplate("custom_note", "Custom note", "<h2>Custom</h2><script>alert(1)</script><p>Safe text</p>")')
            await cmd('templates')
            await page.locator('[data-template-list]').select_option('custom_note')
            page.once('dialog', lambda d: d.accept())
            await page.locator('[data-template-apply]').click()
            result = await html()
            assert 'Custom' in result and 'Safe text' in result and '<script' not in result, result
            print('PASS: table merge/split/row-before/col-before/header, border styles, cropped signature, date, templates')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
