# Compact toolbar — More menu width and alignment (0.8.0)

## Behavior

- **More** is the same height as adjacent toolbar controls (34px desktop, 40px coarse-pointer layout) and centered on the quick-command row.
- The **More** panel spans the editor's visible width, using the editor's left and right borders as its alignment reference. When the editor is partially outside the viewport, the panel is clipped to the available viewport instead of causing horizontal scrolling.
- Wide layouts flow command groups into additional grid columns, instead of leaving a large unused area to the right of a fixed 860px panel. On mobile the groups remain a single vertically scrollable column.
- Fullscreen and viewport resizing recompute panel geometry. The `Escape` key and command callbacks continue to close the menu normally.
- Specific command menus (`File`, `Format`, `Export`, etc.) retain their previous conservative width; table size, checkbox and code-language popovers remain compact.
- The Compact quick-command row is still a single line. The upper text menu is a separate row; no change to toolbar modes or public settings is required.

## Compatibility and scope

This hotfix changes only CSS layout, JavaScript overlay placement, browser tests, and documentation. The server component markup and all editor commands are unchanged. It is intended for the 0.8.0 Fullscreen toolbar hotfix baseline. There is no change to serialized HTML, C# APIs, Blazor interop, or PDF/Word/Excel output.

## Checks

```shell
python tests/editor-more-menu-layout-080-browser.py
python tests/editor-wide-toolbar-browser.py
python tests/editor-fullscreen-080-browser.py
python tests/editor-adaptive-browser.py
```

Also test a real MVC/Razor Pages/Blazor host in Visual Studio at 390px, 1280px and fullscreen, in light and dark appearance. A .NET host test is not replaced by fixture-based Chromium tests.
