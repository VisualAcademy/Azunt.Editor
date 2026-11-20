# Table editing stability (Azunt.Editor 0.7.0 — third patch)

This patch improves the common JavaScript table editing engine used by MVC, Razor Pages, and Blazor Server. It **does not** change stored HTML structure, C# or JavaScript public APIs, stylesheet class names, or the 0.6.8 table grid picker.

## Behavior

- **Caret anchoring:** Row/column insertion, row/column deletion, horizontal merge/split, and header-cell conversion put the caret in the relevant surviving/new cell. Deleting the last row/column or the entire table positions the caret in the surrounding document.
- **Keyboard navigation:** `Tab` moves to the next existing cell and `Shift+Tab` to the previous cell when a collapsed caret is inside a table. At the first/last cell, standard browser focus movement is preserved. No row is automatically added. Embedded inputs/links are not intercepted, and Ctrl/Alt/Meta modifiers are untouched.
- **No data loss when merging:** The content of the right-hand cell is transferred even if it contains only a checkbox, radio button, image, or other nontext element. Only an empty-cell `<br>` placeholder is dropped. Splitting leaves merged content in the first cell, as before.
- **Style continuity:** New rows and columns inherit supported visual styling from their source cells, including borders. Existing cell text and attributes are left untouched.
- **Safety:** Merged tables still reject row/column edits until cells are split. Maximum 12-column merged cells and existing allowlists remain unchanged.
- **Editing history:** Table changes still use the existing Undo/Redo snapshot and selection mechanisms. Edit/View switching retains the same saved HTML. No new dependencies.

## Validation

Run in the repository root:

```powershell
python tests/editor-table-stability-070-browser.py
python tests/editor-table-picker-browser.py
python tests/editor-advanced-browser.py
python tests/editor-undo-070-browser.py
python tests/editor-paste-070-browser.py
```

The new Chromium regression specifically covers form elements during merge, Undo/Redo and Split, caret targets after structural edits, Tab/Shift+Tab, mixed table selection, merged-table restrictions, and Edit/View persistence. Existing editor regressions should also be rerun before packaging.

## Scope / remaining limitations

- This patch does **not** implement rectangular multi-cell drag selection, vertical row-span merge, Excel-like formulas, or automatic row creation at the last table cell.
- Table row/column changes still require simple rectangular tables (no existing colspan/rowspan), and merged cells must be split first.
- PDF/DOCX/XLSX exporters are unchanged. Table export output and .NET 10 Release/host execution must still be validated using the host environment.
