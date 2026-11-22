# Full-document deletion resets formatting

This is a focused, backwards-compatible editing fix for the shared Azunt.Editor JavaScript engine.

## Behavior

- `Ctrl+A` / `Cmd+A` followed by `Delete` or `Backspace` (or Cut) clears all selected document content, including the old heading and inline styling, and inserts a single editable paragraph (`<p><br></p>`).
- The caret moves inside the empty paragraph, the formatting indicator reports **Paragraph**, and the status counter reports **0 words · 0 characters**.
- The hidden HTML value and Blazor change stream receive the same document HTML. The saved HTML no longer contains an empty leftover `<h2>`.
- `Ctrl+Z` restores the original HTML and formatting; `Ctrl+Y` restores the blank paragraph.
- Partial selections and deleting the final character of a heading **without selecting the entire document** preserve the active heading formatting. This is intentional: the user may want to continue typing a heading.
- Media, tables, form controls and horizontal rules must themselves be part of the selection before the whole-document reset occurs. Editing text while such an element is unselected does not erase it.

## Implementation

The editor intercepts cancelable `beforeinput` events of types `deleteContentBackward`, `deleteContentForward`, and `deleteByCut` **only** when the selection covers all document content. Browsers may implement Ctrl+A within `contenteditable` by selecting the first and last **text nodes** rather than a Range covering the root canvas. The detection therefore compares selected text and the counts of non-text document elements, instead of assuming Range endpoints coincide with canvas endpoints.

The operation records its own Undo snapshot, restores a valid caret, clears the stale image/table selection, updates the toolbar and statistics, and uses the existing `changed`/`queueEmit` synchronization path.

For an empty editable paragraph, Chromium's `innerText` may include its caret-placeholder `<br>` as a line break. The counter ignores this placeholder if the document contains no text or non-text content.

## Verification

```bash
python tests/editor-empty-reset-browser.py
```

Also run the existing keyboard, undo/redo, paste, table, mode-switch and public API browser regression tests. This patch changes neither ASP.NET Core model binding nor any exported public API, C# project or CSS file.
