# 0.8.13 — Match document PDF and Word styles

- Optional `EditorExportDocumentStyle` with five built-in presets and validated custom values.
- PDF uses matching typography, table margins, padding, border and header defaults, while retaining print-specific pagination and safety.
- Word maps font sizes to half-points, uses independent horizontal/vertical cell margins, preserves explicit percentage table width and column hints, and translates row height to minimum height. The 0.8.12 caret-break fix remains.
- Blazor demo retrieves the live style ID for both download paths, including toolbar commands. Match-document is the demo default; explicit legacy output choices remain available.
- Existing callers without `DocumentStyle` keep their previous output settings. Excel behavior and editor HTML are unchanged. Both packages remain independent.
- Browser regression checks render the C# CSS template with preset values in Chromium, not the actual .NET exporter. .NET smoke tests cover DOCX package schema, widths, row height, margins, font size, invalid style rejection and opt-in PDF export.
- Limitations: font substitutions, page widths and wrapping differ between renderers. Word still uses printable width when no explicit percentage is set. Nested tables and unsupported HTML/CSS remain subject to existing conversion constraints. Full solution build and DOCX smoke tests require a .NET 10 SDK; actual Microsoft Word visual inspection is recommended.
