# Azunt.Editor — Table dimension picker (0.6.8)

The **Table** split control is available in both Compact and Classic toolbars.

- Click the **▦** primary button to insert the existing 2-column × 2-row table.
- Click the adjacent **▾** button to open a **10-column × 10-row** dimension grid,
  anchored immediately beneath the toolbar control (or above if the viewport
  has insufficient space).
- Hover over a cell to preview the rectangular size; the footer displays
  **columns × rows**. Click to insert a table with that shape.
- For keyboard access, focus the dropdown and press **ArrowDown** to open.
  Use **Arrow** keys to move through the grid, **Home/End** for 1×1 / 10×10,
  **Enter** to insert, or **Escape** to close and return focus.
- On small screens the panel clamps to the visual viewport and scrolls if
  necessary. The grid supports standard pointer/touch clicking.

The table is ordinary HTML, with `tbody`, `tr` and `td` elements and a
`<br>` placeholder per cell. The picker UI itself is never saved in document
HTML. The existing `table` JavaScript command and menu insertion continue to
create a default 2×2 table, preserving old behavior.

This is a presentation-level feature and adds no NuGet/JS dependencies, new
HTML tags, or changes to the server-side sanitizer and export formats.

To verify locally, run `python tests/editor-table-picker-browser.py` with
Playwright and Chromium, then build/run the MVC/Razor Pages/Blazor samples with
.NET 10. In the samples check Edit/View, table edits and exports as before.
