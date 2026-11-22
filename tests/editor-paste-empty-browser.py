"""Regression: rich/plain paste into a document cleared with Ctrl+A/Delete.

Run: python tests/editor-paste-empty-browser.py
Uses Playwright/Chromium, without requiring the ASP.NET Core sample host.
"""
import asyncio
from pathlib import Path

from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[1]
HTML = (ROOT / "tests/editor-adaptive-browser.html").read_text(encoding="utf-8")
JS = (ROOT / "src/Azunt.Editor/wwwroot/js/azunt-editor.js").read_text(encoding="utf-8")
CSS = (ROOT / "src/Azunt.Editor/wwwroot/css/azunt-editor.css").read_text(encoding="utf-8")


async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(
            executable_path="/usr/bin/chromium", headless=True,
            args=["--no-sandbox", "--disable-dev-shm-usage"])
        try:
            page = await browser.new_page()
            errors = []
            page.on("pageerror", lambda error: errors.append(str(error)))
            await page.set_content(HTML)
            await page.add_style_tag(content=CSS)
            await page.add_script_tag(content=JS)
            await page.evaluate('AzuntEditor.mount("browser-test")')
            canvas = page.locator("#browser-test .az-azunt-editor-canvas")

            async def saved():
                return await page.evaluate('AzuntEditor.getHtml("browser-test")')

            async def load(html):
                await page.evaluate('(html) => AzuntEditor.setHtml("browser-test", html)', html)
                await canvas.focus()

            async def empty(key="Delete"):
                await page.keyboard.press("Control+a")
                await page.keyboard.press(key)
                assert await saved() == "<p><br></p>", await saved()

            async def paste(html="", text=""):
                await page.evaluate('''([html, text]) => {
                  const data = new DataTransfer();
                  data.setData('text/html', html);
                  data.setData('text/plain', text);
                  const canvas = document.querySelector('#browser-test .az-azunt-editor-canvas');
                  canvas.dispatchEvent(new ClipboardEvent('paste', {
                    bubbles: true, cancelable: true, clipboardData: data
                  }));
                }''', [html, text])

            original = '<h2><strong>Old heading</strong></h2><p>Old paragraph</p>'
            copied = ('<p>안녕하세요. 박용준입니다.</p><p><br></p>'
                      '<p>반갑습니다.</p><p>또 만나요.</p>'
                      '<p>이것은 Azunt.Editor에서 작성하는 글입니다.</p>')
            await load(original)
            await empty()
            await paste(copied, "안녕하세요. 박용준입니다.\n\n반갑습니다.\n또 만나요.\n이것은 Azunt.Editor에서 작성하는 글입니다.")
            assert await saved() == copied, await saved()
            assert await canvas.locator(':scope > p').count() == 5
            assert (await canvas.locator(':scope > p').first.inner_text()).startswith('안녕하세요')
            await page.keyboard.type("!")
            assert await saved() == copied.replace("글입니다.</p>", "글입니다.!</p>"), await saved()
            print("PASS: Ctrl+A Delete, rich paste, intentional blank line preserved, caret at final paragraph")

            # Only the actual edit snapshots are undoable; no hidden placeholder
            # is introduced when undoing/replaying the paste.
            await page.keyboard.press("Control+z")
            assert await saved() == copied, await saved()
            await page.keyboard.press("Control+z")
            assert await saved() == "<p><br></p>", await saved()
            await page.keyboard.press("Control+z")
            assert await saved() == original, await saved()
            await page.keyboard.press("Control+y")
            assert await saved() == "<p><br></p>", await saved()
            await page.keyboard.press("Control+y")
            assert await saved() == copied, await saved()
            print("PASS: Undo/Redo across full deletion and paste")

            # Rich paste also works on a freshly loaded blank document and does
            # not accidentally carry H2 or other previous formatting into P.
            await load("<p><br></p>")
            await paste("<h2>Heading</h2><p>Text</p>", "Heading\nText")
            assert await saved() == "<h2>Heading</h2><p>Text</p>", await saved()
            await page.keyboard.type("!")
            assert await saved() == "<h2>Heading</h2><p>Text!</p>", await saved()
            print("PASS: Fresh blank document rich paste, heading and paragraph placement")

            # A leading blank that is genuinely part of the clipboard must NOT
            # be stripped as if it came from the empty editor.
            await load("<p>Previous</p>")
            await empty("Backspace")
            with_intentional_blank = "<p><br></p><p>Intentional second line</p>"
            await paste(with_intentional_blank)
            assert await saved() == with_intentional_blank, await saved()
            print("PASS: Explicit leading blank from clipboard is retained")

            # Text/plain and inline-only rich HTML also replace the placeholder
            # <br> without creating a trailing phantom newline.
            await load("<p>Before</p>")
            await empty()
            await paste(text="First\nSecond")
            assert await saved() == "<p>First<br>Second</p>", await saved()
            await page.keyboard.type("!")
            assert await saved() == "<p>First<br>Second!</p>", await saved()
            await load("<p><br></p>")
            await paste(html="<strong>Bold</strong>", text="Bold")
            assert await saved() == "<p><strong>Bold</strong></p>", await saved()
            await page.keyboard.type("!")
            assert await saved() == "<p><strong>Bold!</strong></p>", await saved()
            print("PASS: Plain-text and inline-rich paste; no trailing placeholder")

            # Tables and lists are block content, never nested within a ghost P.
            for fragment in (
                "<table><tbody><tr><td>Cell</td></tr></tbody></table>",
                "<ul><li>First</li><li>Second</li></ul>",
            ):
                await load("<p><br></p>")
                await paste(fragment)
                assert await saved() == fragment, await saved()
                assert await canvas.locator(':scope > p').count() == 0
            print("PASS: Table/list paste into empty document has no empty wrapper paragraphs")

            assert not errors, errors
            print("PASS: No unhandled browser errors")
        finally:
            await browser.close()


if __name__ == "__main__":
    asyncio.run(main())
