# Undo/Redo stability (0.7.0 follow-up)

Azunt.Editor maintains an independent, bounded history **per editor instance**. The
history stores sanitized HTML snapshots; the browser-specific cursor/selection
locations are stored separately. This preserves existing saved HTML and public APIs.

## Improvements

- Undo/Redo restores the caret or selected text after rebuilding the editor DOM.
  If sanitizer changes a node's position, the caret safely falls back to the end
  of the document rather than throwing.
- Standard keyboard shortcuts (`Ctrl+Z`, `Ctrl+Y`, `Ctrl+Shift+Z` on Windows;
  command-key equivalents where supported) continue to use the editor's history.
  Native `beforeinput` `historyUndo/historyRedo` requests also use the same
  history when the browser allows cancellation.
- IME input (including Korean composition) is committed as **one** history
  entry when composition ends. A partially composed syllable is not stored as a
  separate user-visible undo step.
- Applying a fresh edit after Undo removes the obsolete Redo branch.
- Checkbox states, paste operations, formatting changes, and Edit/View cycles
  preserve their existing behavior and history.
- The existing maximum of 60 snapshots and 32 MiB of HTML history remains.
  Large inline image documents may retain fewer snapshots.

## Compatibility and limitations

- No new third-party JavaScript package, NuGet dependency, CSS class, public
  .NET API, or public JavaScript function was added.
- `AzuntEditor.setHtml(...)` intentionally **replaces** content and resets
  history, as in earlier versions.
- HTML source editing uses the browser's native text-area editing while source
  mode is open. Returning to visual mode adds the applied sanitized source as
  a document history entry. Full cross-mode native textarea undo integration is
  not included in this patch.
- Snapshot-based history is not a collaborative or operational-transform undo
  engine and does not restore a user's scroll position.
- Clipboard and HTML content still require server-side validation at the
  application's trust boundary.

## Regression checks

```bash
node --check src/Azunt.Editor/wwwroot/js/azunt-editor.js
python tests/editor-undo-070-browser.py
python tests/editor-paste-070-browser.py
python tests/editor-stability-browser.py
python tests/editor-view-mode-browser.py
python tests/editor-form-controls-browser.py
python tests/editor-table-picker-browser.py
```

The Python browser harness requires Playwright and a local Chromium installation
**for tests only**; it is not a runtime dependency of Azunt.Editor.

A .NET 10 Release build and the actual MVC, Razor Pages and Blazor Server host
executions must still be verified in an environment with the SDK installed.
