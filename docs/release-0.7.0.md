# Azunt.Editor 0.7.0 — Clipboard HTML & Editing Reliability

## Included in this patch

- Word / web clipboard fragment detection (`StartFragment` / `EndFragment`, CF_HTML header).
- Restricted semantic conversion of common inline rich text formatting before applying the existing HTML allowlist.
- Prevention of nested root-level `<p>` / `<table>` when pasting block content into a paragraph; surrounding text and edit history are preserved.
- Harmonized browser and server View HTML element allowlists for standard headings (`h1`/`h4`), table footer (`tfoot`) and deletion formatting (`del`/`strike`).
- Oversized clipboard guard and safe plain-text fallback.
- Added focused Chromium paste regressions and server Viewer smoke assertions.

## Compatibility notes

- No new JavaScript or NuGet dependencies; package IDs and public C#/JS APIs are unchanged.
- Existing custom commands, checked checkbox default, Compact/Classic toolbar, table picker, Blazor initialization behavior, View mode, and PDF/Word/Excel export code remain unchanged.
- This is the **first stabilization pass** for the 0.7.x engine. Advanced table multi-cell selection and future code highlighting are intentionally not reworked by this patch.
- Browser paste results may differ from Word's native rendering because the library intentionally does not import arbitrary Office CSS or remote scripts.

## Test evidence

- New `tests/editor-paste-070-browser.py` passes in local headless Chromium.
- Previously available Chromium regressions (toolbar, forms, tables, images, drag/drop, Edit/View, exports commands, accessibility and dark mode) have been rerun.
- Static JavaScript syntax, release contracts, package policy, CSS namespace and interop checks pass.
- .NET 10 build and real MVC/Razor Pages/Blazor Server host execution are **not verified** in this environment (no dotnet SDK). This patch does not constitute a successful export engine PDF/DOCX/XLSX file generation test.

See [HTML paste handling](html-paste.md) for security boundaries and remaining limitations.

## Second stabilization patch — Undo/Redo

- Adds selection/caret recovery alongside the existing sanitized HTML history.
- Groups IME composition into a single Undo step and consistently routes browser-native history shortcuts.
- Preserves the existing bounded history policy and Edit/View, paste, forms, table, and image behavior.
- Adds focused Chromium tests: `tests/editor-undo-070-browser.py`.
- No C#/NuGet API changes; actual .NET 10 Release build and host execution remain unverified here.

See [Undo/Redo stability](undo-redo-stabilization.md).

## Third stabilization patch — Table Editing

- Keeps nontext content (including checked checkboxes and radio buttons) during horizontal cell merging.
- Restores a usable caret after row/column add/delete, header toggle, merge/split and table delete.
- Supports Tab/Shift+Tab navigation within existing table cells, without overriding boundary focus or embedded input controls.
- Copies permitted visual cell styles into newly inserted rows/columns.
- Adds focused regression tests in `tests/editor-table-stability-070-browser.py`.
- No public API/schema/dependency changes or edits to PDF/Word/Excel exporters.

See [Table editing stability](table-editing-stabilization.md).

## Fourth stabilization patch — Images and HTML roundtrip

- Captures insertion caret before asynchronous image reads/uploads; prevents
  stale insertion after `setHtml`, source editing, View transitions or unmount.
- Preserves selected file order and image insertion size setting per batch.
- Counts Base64 decoded bytes accurately for the existing inline image budget.
- Cancels incomplete image resize before serializing a View-mode document.
- Retains image HTML (`src`, `width`, `height`, `alt`) and existing image stores.
- Adds `tests/editor-image-persistence-070-browser.py`; does not change C# public
  APIs, stored HTML schema or PDF/Word/Excel exporter implementations.

See [Image persistence](image-persistence.md). The .NET 10 build, real host
integration and generated export formats remain unverified in this environment.

## Fifth stabilization patch — Table move and column resizing

- Adds a non-document table move grip and a column boundary resize grip in Edit mode.
- Whole-table drag/reorder is constrained to top-level blocks; merged cells never permit destructive column resizing.
- Stores bounded percentage cell widths and `table-layout:fixed`; browser and server View/export sanitizers share the allowlist.
- Undo/Redo, keyboard controls, Edit/View cancellation, and column add/remove normalization are covered by dedicated Chromium and .NET smoke assertions.
- See [Table movement and column resizing](table-movement-and-column-resizing.md).
- .NET 10 Release build and real host/export generation remain unverified in the current environment.
