# Azunt.Editor 0.8.0 — Table movement and column grips hotfix

## Fixed behavior

- A table inserted via the normal toolbar while the caret is within a paragraph is created as a sibling block, not nested within `<p>`. Text before and after the caret remains in the document.
- Pre-existing tables wrapped in a regular `<div>` or paragraph can display movement/column resize grips. Nested tables inside another table are deliberately excluded to protect the outer table structure.
- Clicking the last cell in a row keeps the final *internal* column boundary selectable for resizing.
- Compact mode's floating table command toolbar is positioned outside the whole table rather than on top of adjacent cells.
- Floating contextual commands are hidden while a table or column is being dragged, then restored on completion. This prevents them from intercepting pointer events or the drop target.
- Keyboard movement of a table with a legacy wrapper uses top-level canvas blocks to avoid invalid DOM `insertBefore` calls.

No public C# API, NuGet dependency, CSS class, saved HTML format, or PDF/Word/Excel exporter was changed.

## Local verification

1. Run the web sample (`/editor/blazor`, `/editor/mvc`, or `/editor/razor-pages`) and insert a table inside an existing paragraph. Click its first cell. Both the movement grip and the column separator should be shown without refreshing.
2. Drag the movement grip to the next paragraph; verify the table contents remain and Undo restores the previous location.
3. Click the final column, drag the separator left/right, save HTML, and switch Edit → View → Edit. Confirm percentages survive and Undo works.
4. Confirm a cell-merged table remains movable but its column resize grip is not displayed.
5. Confirm Export and existing editor operations still work after editing a table.

## Browser regression

```bash
python tests/editor-table-grip-hotfix-080-browser.py
python tests/editor-table-drag-resize-070-browser.py
python tests/editor-table-stability-070-browser.py
```

The first test covers both a .NET-host-rendered grip layout and the JavaScript fallback layout.

.NET 10 Release build and actual host page validation were not performed in the patch assembly environment.
