# Azunt.Editor 0.8.0 — Document Export Quality (first pass)

## Changes

- PDF print template has no unintended browser body margin. Code backgrounds are printed consistently, and long code preserves tabs and wraps across pages.
- Word output now writes native line breaks (`w:br`) and tab runs (`w:tab`) for preformatted code, sets a monospace font and a light code background, and respects validated percentage widths on HTML table cells.
- Excel output writes code **one logical source line per editable cell**, instead of hiding a long code sample inside a single height-limited cell. Very long individual lines are split into bounded segments to respect Excel's 32,767-character cell limit.
- Excel adds a minimal style palette for code rows and table header rows, and uses valid, complete first-row percentage widths when available.
- Both Open XML outputs continue treating code and formula-looking input as **text**, not executable material.

## Compatibility

- Saved HTML, public editor APIs, existing HTML sanitizer policies, image resolver, and PDF browser installation strategy are unchanged.
- New Excel style records are internal; the workbook remains editable. Code blocks no longer occupy a single tall Excel cell; this is a deliberate layout change.
- Syntax coloring in Word/Excel is not part of this patch. PDF renders printable code style but does not execute JavaScript in Playwright. Inline and host-resolved PNG/JPEG images retain their current export behavior.
- Simple cells with validated percent width map to Word table cell widths. Merged-cell and mixed-width tables retain current conservative behavior. Excel maps complete percentage distributions on the first **unmerged** row to approximate column widths; Excel column width units differ from HTML pixels.

## Validation

- Run `dotnet test` / `dotnet run --project tests/Azunt.Editor.Export.SmokeTests` and test a real PDF browser on the target operating system. To include the opt-in PDF smoke path, set `AZUNT_TEST_PDF=1` and install the matching Playwright browser.
- Run existing editor Chromium regressions as in README. Build and packaging must be checked with .NET 10 SDK in the host environment.

## Toolbar layout follow-up (0.8.0)

- Visually joined the table icon and its size-picker arrow into a split button.
- Moved the checked-checkbox and code split controls into Compact's Insert group.
- Normalized the height and adjacent dropdown styling of Table, Form and Code
  in Compact and Classic toolbars, including dark mode and touch targets.
- Kept all existing command handlers, HTML storage, and export API unchanged.
- Added `tests/editor-toolbar-split-080-browser.py` for layout and UI command regressions.

See [toolbar split controls](toolbar-split-controls-0.8.0.md).

## Fullscreen toolbar follow-up (0.8.0)

- Exposed the existing fullscreen command as a permanent right-aligned Compact
  toolbar icon, including in reduced-width layouts. Classic retains its existing command.
- Kept accessible pressed states and labels synchronized between toolbar modes,
  restored focus to the invoking control after Escape, and automatically exited
  fullscreen when changing Edit → View.
- The implementation remains editor-only CSS fullscreen (no browser fullscreen
  permissions) and does not alter saved HTML, Export or public .NET APIs.
- Added `tests/editor-fullscreen-080-browser.py` covering Compact, Classic,
  narrow screens, dark mode, Escape and mode transitions.

See [fullscreen toolbar](fullscreen-toolbar-0.8.0.md).
