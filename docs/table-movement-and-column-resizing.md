# Table movement and column resizing (0.7.0 follow-up)

## Table movement

In Edit mode, hover over a table or click one of its cells to display the small **Move table** grip at its upper-left edge. Drag the grip to the space above or below another document block; the blue line marks the destination. Release to move the **whole table**, including its cells, inputs, and images. Use **Alt+Up** or **Alt+Down** while the grip has focus to move the table one block. Use **Ctrl+Z** to undo.

- A table is moved **between top-level document blocks** (paragraphs, headings, tables). It is not inserted inside another table cell, nor does it reorder individual rows.
- Tables nested in pasted wrapper elements are intentionally not draggable until made top-level; the original document content is retained.
- Dropping outside the editor, on the same position, or pressing **Escape** cancels without changing the HTML.

## Column resizing

In Edit mode, select a cell or hover near the right edge of a cell. Drag the **blue vertical column grip** to distribute width between the adjacent columns without changing the table's total width. **Left/Right arrow** on the focused grip adjusts the width by approximately 8 pixels per keypress. Use Ctrl+Z to undo.

- Available for simple rectangular tables with **2–10 columns**, without merged cells or row spans. Merge/split remains available as before; column resizing is disabled for merged tables to prevent corrupt geometry.
- Cells keep a minimum width, and columns are represented as bounded percentages on `<td>` / `<th>` with `table-layout:fixed` on `<table>`.
- HTML sanitize routines for the browser, server View, and export HTML allow only percentage widths (0–100 with at most 3 decimal places) and the literal `table-layout:fixed`. Arbitrary styles, script/event attributes, and resize UI are not saved.
- The dedicated grips are **outside `contenteditable`** and are not present in `getHtml()`, the form value, View markup, or exported HTML.
- Moving a table or finishing a column drag commits one Undo snapshot; canceled gestures do not commit changes. A mode switch cancels incomplete gestures before serializing.

## Compatibility / limitations

- Existing table insert picker, Merge/Split, row/column add/delete, checkbox inputs, Undo/Redo, image handling, paste, Edit/View, and registered Export commands are unchanged.
- When adding or removing columns in a resized table, widths are normalized to 100% instead of carrying invalid percentages into the saved document. Rows inherit permitted cell widths.
- HTML View / browser printing / PDF HTML preserve safe widths. DOCX and XLSX converters retain the table and cell data, but **precise pixel-equivalent column widths are not guaranteed** by those output formats yet.
- No public JavaScript or C# APIs, NuGet packages, or external editor libraries were added.

## Verification

Run `python tests/editor-table-drag-resize-070-browser.py` and the existing Chromium browser tests. Validate Release builds and MVC/Razor Pages/Blazor hosts with the local .NET 10 SDK: those runtime validations are not available in the patch-generation environment.
