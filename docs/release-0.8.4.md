# Azunt.Editor 0.8.4 — PDF print profiles

Both NuGet packages and the sample package references are updated to 0.8.4.
No new dependencies or changes to existing package versions, target framework,
HTML editor JavaScript, CSS, persistence, image resolver or sanitizer.

## PDF-only options

```csharp
using Azunt.Editor.Export.Models;

var result = await exporter.ExportAsync(html, EditorExportFormat.Pdf,
    new EditorExportOptions
    {
        FileName = "Operations-Report",
        Title = "Operations Report",
        PaperSize = "Letter",
        Orientation = "portrait",
        Margin = "normal",
        PdfLayout = EditorPdfLayout.Compact,
        // Optional overrides, in points / CSS pixels:
        PdfFontSize = 10,
        PdfTableFontSize = 9,
        PdfTableCellPadding = 3,
        PromotePdfTableHeaders = true,
        ShowPageNumbers = true
    });
```

| Profile | Default body | Default tables | Cell padding | Line height |
|---|---:|---:|---:|---:|
| Standard (default) | 12pt | inherits body | 6px | 1.45 |
| Compact | 10pt | 9pt | 3px | 1.2 |
| Comfortable | 14pt | 12pt | 8px | 1.6 |

- Font overrides accept integers from 8 to 36 pt; padding from 0 to 24 CSS px.
  Invalid values or enum values throw `ArgumentOutOfRangeException` for PDF
  only, before launching Playwright. Null means the profile's default.
- Standard without overrides emits the original stylesheet verbatim. Existing
  callers do not need changes. Word and Excel ignore the added PDF options.
- Compact uses thinner default borders and smaller table/paragraph spacing;
  safe explicit inline styles on tables, cells and text remain authoritative.
  An explicit font inherited from outside a table may be superseded by the
  table default. Existing sanitizer limits remain (e.g. supported font sizes
  are integer CSS px, not arbitrary CSS). No unsafe style input is accepted.
- Explicit semantic `thead` repeats on printed table pages using the existing
  CSS. `PromotePdfTableHeaders` (false by default) moves an initial all-`th`
  row to a print-only `thead` for simple direct-row or tbody tables without an
  existing header. It does not guess headers from `td` cells or mixed rows.
  Only the parsed per-export document is adjusted; original HTML is unchanged.
- Existing row-break protection remains best effort; oversized rows, unusually
  wide tables, fixed/explicit widths and long tokens can still wrap or split.
  The library does not force all tables into one page or truncate content.
  This release does not change rowspan support or automatically recalculate
  application-specific column widths. Use appropriate width hints or landscape
  for wide tables; no arbitrary global nowrap rule is introduced.
- Existing A4/Letter/Legal, portrait/landscape, narrow/normal/wide margins and
  optional page count footer are reused unchanged.
- Browser launch, offline context, concurrency limits, image safeguards and
  cancellation behavior are unchanged. Hosts must supply a compatible browser.

## Independent demo

Open `/editor/export` from the Azunt.Web navigation or home page. Load a simple
sample document, 12-row report, 80-row report or image/signature sample. All
fixtures are synthetic. Change PDF profile, paper, orientation, margins,
optional font/padding and header/footer options. Export buttons and the editor's
Export menu both read current HTML using the existing streaming API. Word and
Excel use the same current HTML; PDF density settings do not affect them.
The demo's explicit export settings override toolbar page settings on this page
only. No application-specific integration or external storage is required.

## Validation / release gate

```powershell
dotnet build Azunt.Editor.sln -c Release
dotnet run --project tests/Azunt.Editor.SmokeTests -c Release
dotnet run --project tests/Azunt.Editor.Export.SmokeTests -c Release
# Install the version-matched Playwright browser if not already available.
pwsh tests/Azunt.Editor.Export.SmokeTests/bin/Release/net10.0/playwright.ps1 install --only-shell chromium
$env:AZUNT_TEST_PDF = '1'
$env:AZUNT_TEST_PDF_OUTPUT = (Join-Path (Get-Location) 'pdf-smoke-output')
dotnet run --project tests/Azunt.Editor.Export.SmokeTests -c Release
# Optional Python browser tests (pip install playwright pymupdf)
python tests/editor-export-084-pdf-profiles.py
python tests/editor-export-080-pdf-layout.py
python tests/editor-export-081-pdf-color-layout.py
python tests/verify-release-contracts.py
python tests/verify-package-policy.py
dotnet run --project samples/Azunt.Web
```

The 0.8.4 Python test uses actual CSS with a synthetic semantic table; when
`AZUNT_TEST_PDF_OUTPUT` is set, it also checks actual .NET-generated PDF files
for all 80 records, repeated headers (including the new promotion path) and
relative page counts. The browser-only test by itself does not exercise C#,
sanitization, browser installation or Azure hosting.

Manually open exported files, compare all three PDF profiles, check page
numbers, inline font overrides, landscape, image/signature content and
multi-page tables. Recheck MVC, Razor Pages and Blazor existing demos.
Test `dotnet pack` and package-reference mode using a local feed before public
NuGet publication. Package mode requires the new packages to be available.
