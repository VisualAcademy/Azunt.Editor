"""Status lifetime and accessibility regression test (Chromium + Python Playwright).
Run: python tests/editor-status-browser.py
"""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
JS = (ROOT / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css').read_text(encoding='utf-8')
HTML = (ROOT / 'tests/editor-adaptive-browser.html').read_text(encoding='utf-8')

async def main():
    async with async_playwright() as pw:
        launch = {'headless': True}
        if os.getenv('AZUNT_CHROMIUM_PATH'):
            launch['executable_path'] = os.environ['AZUNT_CHROMIUM_PATH']
        elif Path('/usr/bin/chromium').exists():
            launch['executable_path'] = '/usr/bin/chromium'
        if os.name != 'nt':
            launch['args'] = ['--no-sandbox', '--disable-dev-shm-usage']
        browser = await pw.chromium.launch(**launch)
        page = await browser.new_page(viewport={'width': 1220, 'height': 820})
        await page.clock.install()
        errors = []
        page.on('pageerror', lambda err: errors.append(str(err)))
        try:
            await page.set_content(HTML)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            status = page.locator('#browser-test .az-azunt-editor-message')
            async def show(text, kind):
                await page.evaluate('([text,kind]) => AzuntEditor.showStatus("browser-test", text, kind)', [text,kind])
            async def check(text, kind, role):
                assert await status.text_content() == text, await status.text_content()
                assert await status.get_attribute('data-status-kind') == kind
                assert await status.get_attribute('role') == role
                assert await status.get_attribute('aria-live') == ('assertive' if role == 'alert' else 'polite')

            await show('Signature inserted.', 'success')
            await check('Signature inserted.', 'success', 'status')
            await page.clock.run_for(2000)
            assert await status.text_content() == 'Signature inserted.'
            await page.clock.run_for(1200)
            assert await status.text_content() == ''
            assert await status.get_attribute('data-status-kind') is None

            await show('Working on your document.', 'info')
            await page.clock.run_for(2500)
            assert await status.text_content() == 'Working on your document.'
            await page.clock.run_for(1600)
            assert await status.text_content() == ''

            # The first timer may not clear a more recent notification.
            await show('Old notice', 'success')
            await page.clock.run_for(1200)
            await show('Select a cell first.', 'warning')
            await check('Select a cell first.', 'warning', 'status')
            await page.clock.run_for(1900)
            await check('Select a cell first.', 'warning', 'status')
            await page.clock.run_for(3200)
            assert await status.text_content() == ''

            await show('Unable to save document.', 'error')
            await check('Unable to save document.', 'error', 'alert')
            await page.clock.run_for(65000)
            await check('Unable to save document.', 'error', 'alert')
            await show('Saved.', 'success')
            await check('Saved.', 'success', 'status')
            await show('', 'info')
            assert await status.text_content() == ''

            # Actual editor command should emit warning, not a permanent error.
            await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Classic")')
            await page.locator('[data-command="clear-format"]').click()
            await check('Select the text whose formatting you want to clear.', 'warning', 'status')
            await page.clock.run_for(5100)
            assert await status.text_content() == ''
            await page.locator('[data-command="horizontal-rule"]').click()
            await check('Horizontal line inserted.', 'success', 'status')
            assert 'Horizontal line inserted.' not in await page.evaluate('AzuntEditor.getHtml("browser-test")')

            # A failed custom command produces a persistent error.
            await page.evaluate('''() => AzuntEditor.registerCommand({
                id:'failingCommand',label:'Failing action',group:'tools',
                execute:()=>{throw Error('Deliberate test failure');}
            }, 'browser-test')''')
            await page.evaluate('AzuntEditor.setToolbarMode("browser-test", "Compact")')
            await page.locator('[data-azunt-menu="tools"]').click()
            await page.locator('[data-menu-panel] [data-custom-command="failingCommand"]').click()
            await check('Command failed.', 'error', 'alert')
            await page.clock.run_for(11000)
            await check('Command failed.', 'error', 'alert')
            await show('<b>Visible as text</b>', 'info')
            assert await status.text_content() == '<b>Visible as text</b>'
            assert await status.locator('b').count() == 0

            # Each editor owns its own timer and messages.
            await page.evaluate('''() => {
                const root = document.querySelector('#browser-test');
                const another = root.cloneNode(true);
                another.id = 'another-editor';
                root.after(another);
                AzuntEditor.mount('another-editor');
            }''')
            await show('Primary status', 'error')
            await page.evaluate('AzuntEditor.showStatus("another-editor", "Second editor", "success")')
            await page.clock.run_for(3100)
            assert await page.locator('#another-editor .az-azunt-editor-message').text_content() == ''
            await check('Primary status', 'error', 'alert')

            # Unmount + remount must not leave old timers that clear new notifications.
            await show('Before remount', 'success')
            await page.evaluate('''() => {
                AzuntEditor.unmount('browser-test');
                AzuntEditor.mount('browser-test');
                AzuntEditor.showStatus('browser-test', 'After remount', 'error');
            }''')
            await page.clock.run_for(6000)
            await check('After remount', 'error', 'alert')
            assert not errors, errors
            print('PASS: status lifetimes, errors, overlapping timers, commands, accessibility, multiple instances, remount')
        finally:
            await browser.close()

if __name__ == '__main__':
    asyncio.run(main())
