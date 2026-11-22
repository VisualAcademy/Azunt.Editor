"""Regression coverage for full-document deletion and formatting reset.

Run: python tests/editor-empty-reset-browser.py
Requires Playwright for Python and Chromium; no .NET host is needed.
"""
import asyncio
from pathlib import Path

from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = ROOT / "tests/editor-adaptive-browser.html"
CSS = ROOT / "src/Azunt.Editor/wwwroot/css/azunt-editor.css"
JS = ROOT / "src/Azunt.Editor/wwwroot/js/azunt-editor.js"


async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(
            executable_path="/usr/bin/chromium", headless=True,
            args=["--no-sandbox", "--disable-dev-shm-usage"])
        try:
            page = await browser.new_page()
            errors = []
            page.on("pageerror", lambda err: errors.append(str(err)))
            await page.set_content(HTML.read_text(encoding="utf-8"))
            await page.add_style_tag(content=CSS.read_text(encoding="utf-8"))
            await page.evaluate('''() => {
              const e = document.querySelector('#browser-test');
              const label = document.createElement('span');
              label.dataset.currentFormat = '';
              e.appendChild(label);
            }''')
            await page.add_script_tag(content=JS.read_text(encoding="utf-8"))
            canvas = page.locator("#browser-test .az-azunt-editor-canvas")

            async def load(html, selector=None, start=None, end=None):
                await page.evaluate('''({html, selector, start, end}) => {
                  AzuntEditor.setHtml('browser-test', html);
                  const canvas = document.querySelector('.az-azunt-editor-canvas');
                  canvas.focus();
                  if (selector) {
                    const block = canvas.querySelector(selector);
                    const node = block.firstChild;
                    const range = document.createRange();
                    range.setStart(node, start === null ? 0 : start);
                    range.setEnd(node, end === null ? (start === null ? 0 : start) : end);
                    const selection = window.getSelection();
                    selection.removeAllRanges();
                    selection.addRange(range);
                    block.dispatchEvent(new KeyboardEvent('keyup', {bubbles: true}));
                  }
                }''', {"html": html, "selector": selector, "start": start, "end": end})

            async def saved():
                return await page.evaluate("AzuntEditor.getHtml('browser-test')")

            async def full_delete(html, key):
                await load(html, 'h2' if '<h2' in html else None)
                await page.keyboard.press('Control+a')
                await page.keyboard.press(key)
                assert await saved() == '<p><br></p>', (key, html, await saved())
                assert await page.locator('[data-current-format]').inner_text() == 'Paragraph'
                assert await page.locator('[data-editor-stats]').inner_text() == '0 words · 0 characters'

            # Both Delete and Backspace must reset all block/inline formatting,
            # including when Chromium selects text nodes instead of whole blocks.
            for key in ('Delete', 'Backspace'):
                await full_delete('<h2><br></h2><h2 style="margin-left: 0px"><strong>sdfs</strong></h2><h2><br></h2>', key)
                await page.keyboard.type('Fresh')
                assert await saved() == '<p>Fresh</p>', await saved()
                assert 'Heading' not in await page.locator('[data-current-format]').inner_text()
            print('PASS: Ctrl+A Delete/Backspace resets heading, empty wrappers and typing style')

            # The original document and all of its formatting must be undoable.
            original = '<h2><strong>Restore title</strong></h2><p>Second block</p>'
            await full_delete(original, 'Backspace')
            await page.keyboard.press('Control+z')
            assert await saved() == original, await saved()
            await page.keyboard.press('Control+y')
            assert await saved() == '<p><br></p>', await saved()
            await page.wait_for_timeout(260)
            assert await page.locator('[data-azunt-value]').input_value() == '<p><br></p>'
            print('PASS: Undo/Redo and form model binding')

            # Deleting only some or all of the text in a single heading, while
            # unrelated content remains, must not clear its block formatting.
            await load('<h2>Title</h2><p>Keep me</p>', 'h2', 0, 5)
            await page.keyboard.press('Delete')
            assert '<h2>' in await saved() and '<p>Keep me</p>' in await saved(), await saved()
            await load('<h2>X</h2>', 'h2', 0, 0)
            await page.keyboard.press('Delete')
            assert await saved() == '<h2><br></h2>', await saved()
            print('PASS: Partial deletion and final-character deletion retain the active heading')

            # A text selection must not inadvertently discard another block's
            # image, even if that selection covers every text character.
            image = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+8qV0AAAAASUVORK5CYII='
            await load(f'<h2>Title</h2><p><img src="{image}" alt="pixel"></p>', 'h2', 0, 5)
            await page.keyboard.press('Delete')
            assert '<img ' in await saved(), await saved()
            await load('<p>Paragraph</p><h2>Heading</h2>', 'h2', 0, 0)
            await page.keyboard.press('Control+a')
            await page.keyboard.press('Delete')
            assert await saved() == '<p><br></p>', await saved()
            await load('<table><tbody><tr><td>Cell</td></tr></tbody></table>')
            await page.keyboard.press('Control+a')
            await page.keyboard.press('Delete')
            assert await saved() == '<p><br></p>', await saved()
            print('PASS: Unselected media survives; selected multiple blocks/tables reset')

            # Browser cut is also a full-document deletion when everything is selected.
            await load('<h2>Cut this</h2>', 'h2', 0, 0)
            await page.keyboard.press('Control+a')
            await page.keyboard.press('Control+x')
            assert await saved() == '<p><br></p>', await saved()
            print('PASS: Ctrl+A Cut resets the document')

            # Empty native-style documents never acquire stale heading markup.
            await load('<p><br></p>')
            await page.keyboard.type('A')
            assert await saved() == '<p>A</p>', await saved()
            assert not errors, errors
            print('PASS: Existing empty paragraph editing; no unhandled browser errors')
        finally:
            await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
