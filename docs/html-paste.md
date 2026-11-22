# HTML paste handling — Azunt.Editor 0.7.0

## Scope

The editor intercepts `paste` (and supported HTML/text drop) in **Edit** mode.
Clipboard HTML is never injected directly into the live editor. The client:

1. Extracts `<!--StartFragment-->` / `<!--EndFragment-->` if present, or removes the usual Windows CF_HTML header.
2. Converts recognized HTML/Word/Google Docs inline formatting (`font-weight`, `font-style`, `text-decoration` and a small legacy `<font>` subset) to ordinary safe semantic formatting.
3. Reuses the existing editor allowlist for HTML elements, attributes, image references, colors and dimensions. Office classes, `mso-*` declarations, scripts, forms and event handlers are not persisted.
4. Inserts the sanitized fragment at the selection. When a block-level fragment is pasted into a root paragraph or heading, splits that element so tables/paragraphs do not become invalid nested children. The text before and after the caret remains in the document.
5. Records the operation in undo history, schedules the existing HTML changed event, and uses the ordinary Edit/View serialization.

This does **not** add a third-party editor library or modify host application APIs. It does not alter the semantics of `setHtml()`, direct HTML source editing or document exports. `h1`, `h4`, `del`, `strike` and `tfoot` are now included in the editor/Viewer HTML allowlists, matching the Export parser.

## Pasting into an empty document

After **Ctrl+A** followed by **Delete** or **Backspace**, Azunt.Editor keeps
`<p><br></p>` as a caret placeholder and resets the active paragraph format.
That placeholder is not document content. When HTML containing block elements
is pasted into that empty document, the clipboard blocks replace it instead
of being inserted between two empty paragraphs. The cursor is placed at the
end of the pasted content so typing can continue in its final editable block.

Inline HTML and plain-text paste replace the placeholder `<br>` *inside* the
paragraph. Intentional blank lines actually present in the pasted HTML or
text remain untouched. Insertion into non-empty paragraphs still uses the
existing block-splitting logic; neither the sanitizer nor the public API is
changed. The same behavior applies to the shared editing engine in MVC,
Razor Pages and Blazor Server.

Regression: `python tests/editor-paste-empty-browser.py` checks full deletion,
rich/plain paste, deliberate blank lines, caret placement, and Undo/Redo.

## Security

The browser allowlist is **not a trust boundary**. For untrusted documents, applications must validate/sanitize HTML **on the server before saving and when rendering**. The viewer uses `EditorViewerSanitizer`; export has `EditorHtmlSanitizer`. Neither grants authorization to remote image URLs or unsafe link schemes. Host applications remain responsible for CSP, authentication, storage limits and trusted image URL validation.

Pasted images support only the existing permitted image URL types. Image files continue to use the established image insertion limit/storage flow. Rich HTML pasted with unsupported tags or controls is stripped; where it contains no usable content and the clipboard supplies plain text, that text is inserted instead.

## Known limits (intentional)

- No attempt to reproduce Word stylesheets, themes, tracked changes, smart art or complex `mso-list` formatting. Tables and inline formatting are retained only within the editor's narrow supported subset.
- Selection spanning multiple complex table cells or list items still uses the browser's existing selection/insertion behavior; this stage deliberately avoids rewriting the entire selection engine.
- Rich paste cannot guarantee pixel-identical rendering to the source application. Native browser undo and editing behavior vary by browser; regression tests use Chromium.
- `PDF`, `DOCX`, and `XLSX` export engines are not changed in this stage; export generation must be verified separately in a .NET host.

## Regression tests

Run from repository root:

```powershell
python tests/editor-paste-070-browser.py
python tests/editor-advanced-browser.py
python tests/editor-drop-browser.py
python tests/editor-form-controls-browser.py
python tests/editor-066-view-export-browser.py
python tests/editor-stability-browser.py
python tests/verify-release-contracts.py

dotnet restore Azunt.Editor.sln
dotnet build Azunt.Editor.sln -c Release
dotnet run --project tests/Azunt.Editor.SmokeTests -c Release
dotnet run --project tests/Azunt.Editor.Export.SmokeTests -c Release
```

Browser tests require a local Chromium and Playwright for **the test runner only**. No new production dependencies have been added.
