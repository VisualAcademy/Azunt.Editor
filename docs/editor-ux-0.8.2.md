# Azunt.Editor 0.8.2 — Editor UX Refinement

This version adjusts *viewport positioning* and accessibility only.

- All command dialogs and contextual tools are positioned in viewport coordinates in **both** Compact and Classic toolbar modes. Previously Classic menus could be displaced after a page scroll because the calculation was viewport-based but the element was absolute-positioned.
- More chooses above/below by comparing available space to actual menu content height (capped at 560px for More, 440px for other menus). It remains clipped to the visible viewport.
- Open command dialogs have context-sensitive accessible names. Keyboard focus visibility is improved, and touch button targets are at least 40px tall in the menu.
- Existing behaviors, including Escape to close, arrows to navigate, Fullscreen, Edit/View, table grid picker, export commands, and toolbar height on mouse/desktop are unchanged.

## Validation

`python tests/editor-ux-082-browser.py` tests classic scrolled positioning, toolbar modes, More near viewport bottom, keyboard focus, dark/light appearance and touch size. Other regression tests should also be run. Real .NET hosts and release builds require .NET SDK and are not implied by browser tests.
