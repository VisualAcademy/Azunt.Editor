# 0.8.12 — Editor view options and Word empty-cell spacing

- Optional fixed content viewport with internal scrolling; AutoGrow remains the default.
- Independent display zoom from 50 to 200 percent (100 percent default).
- Optional view controls in the editor toolbar/menu; Blazor demo shows both options.
- Zoom-aware table and image resize calculations. HTML and browser print output are not zoomed.
- Word: treat a solitary HTML caret-placeholder `br` in a blank paragraph or cell as a single empty Word paragraph. Preserve multiple explicit breaks and breaks between content. Apply the chosen table font size to paragraph marks, including empty cells.
- No new dependencies. Existing document styles remain independent of view options.

## API

Blazor: `HeightMode`, `ZoomPercent`, `ShowViewOptions`, `HeightModeChanged`, `ZoomPercentChanged`, `SetViewOptionsAsync(mode, height, zoomPercent)`.
MVC/Razor Pages tag helper: `height-mode="Fixed" height="600" zoom-percent="100" show-view-options="true"`.
JavaScript: `AzuntEditor.setViewOptions(id, "Fixed", 600, 100)` and `AzuntEditor.getViewOptions(id)`.

Height is in CSS pixels; zoom is a view setting, not a font-size change. Page-layout pagination and server export style mapping are separate future tasks.

## Verification

Chromium viewport, zoom, source, edit/view, print isolation, table and image resize tests pass. Existing custom-style, print, image and hydration browser regressions pass. C# build, DOCX smoke tests and NuGet pack need to be run on a .NET 10 environment; actual Word rendering has not been verified here.
