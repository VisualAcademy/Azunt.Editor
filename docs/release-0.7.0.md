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
