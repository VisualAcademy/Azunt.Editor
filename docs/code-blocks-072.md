# Code block UX refinements (0.7.2)

## Editing

The Code toolbar opens the existing code textarea. In that textarea:

- **Enter** preserves the current line's indentation. After an opening `{`, `(` or `[`, it also adds four spaces.
- **Tab** inserts four spaces. When multiple lines are selected it indents the selected lines.
- **Shift+Tab** removes up to four leading spaces (or one tab) from the current or selected lines.
- **Escape** closes the panel without applying its contents.

These shortcuts affect **only the code textarea**; ordinary rich-text editing shortcuts are unchanged. Automatic indentation is deliberately simple (not a compiler/parser); it does not reformat an entire document or auto-close braces.

## Line numbers, display, and persistence

- Code blocks have a non-editable, `aria-hidden` line-number gutter after JavaScript initialization.
- Line numbers stay visible during horizontal scrolling on long lines; the original `<code>` text remains unchanged.
- The gutter is omitted if a block has **more than 5,000 lines** to prevent slow document rendering; content remains intact.
- The original HTML remains `<pre class="az-code-block" data-language="..."><code>escaped code text</code></pre>`.
- Token spans, line numbers, and Edit/Copy buttons are **not** included in `getHtml()`, saved content, Undo/Redo, or code Copy.
- Static SSR and JavaScript-disabled pages show plain preformatted code without line numbers or colors. This preserves no-JS content readability.

## Highlighting

C#, JavaScript, SQL, HTML, and CSS have dependency-free token coloring for supported keywords, comments, strings, numbers, type-like identifiers, HTML tags/attributes, and CSS properties. It is an approximate highlighter, **not** a complete language grammar or parser. Its output is display-only; source is always encoded text and never evaluated.

## Printing and exports

- Browser print mode hides edit/copy controls and gutters, wraps long lines, and avoids clipped horizontal content.
- The Playwright PDF export template applies wrapping and allows long code blocks to flow onto additional pages.
- Word/Excel continue to export the original code text. Token colors and line numbers are not embedded into PDF/Word/Excel; PDF styling is intentionally optimized for legibility.
- Image, table, form controls, Edit/View, and the Export command-registration API are unchanged.

## Tests

```bash
python tests/editor-code-blocks-071-browser.py
python tests/editor-code-blocks-072-browser.py
python tests/verify-release-contracts.py
# .NET 10 SDK required:
dotnet build Azunt.Editor.sln -c Release
```

Test on an actual MVC/Razor Pages/Blazor Server host and verify the real PDF, DOCX and XLSX downloads before publishing NuGet. Tests running only in Chromium are not substitutes for end-to-end .NET export validation.
