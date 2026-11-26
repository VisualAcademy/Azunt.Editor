# Content-first keyboard navigation

In Edit mode, Tab from an adjacent form field moves directly to the document content (or the visible HTML source textarea). Tab and Shift+Tab follow the browser's normal field order without visiting the closed toolbar. No host configuration is needed. Compact (including a hidden menu row) and Classic both use this behavior.

- Press **Alt+F10** in the editor to access the toolbar. Compact focuses the menu bar or More when the menu row is hidden; Classic focuses its first visible enabled control. Use Tab/Shift+Tab to navigate the toolbar; the existing menu and grid arrow keys remain available.
- Press **Escape** to return from the toolbar to the editing surface, preserving the saved text selection where possible. An open menu closes first and restores its opener; Escape again returns to content. Fullscreen's Escape behavior from the content remains unchanged.
- The toolbar becomes excluded from the sequential Tab order again when focus leaves it. Mouse and touch commands continue to work, including dynamically registered custom commands.
- Table cell navigation, code textarea indentation, document links/form controls, selected image/table grips and open editing panels retain their existing keyboard behavior. Read-only Edit retains its previous toolbar access (e.g. print); View continues to hide the toolbar.

## Verification

- `python tests/editor-content-first-browser.py` (Playwright and Chromium)
- `python tests/editor-keyboard-browser.py`
- `python tests/editor-table-stability-070-browser.py`
- `dotnet run --project tests/Azunt.Editor.SmokeTests`

The patch does not change the package version or public APIs. Rebuild the package and refresh the consuming application's static assets when deploying.
