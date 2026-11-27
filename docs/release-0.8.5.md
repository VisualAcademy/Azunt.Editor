# Azunt.Editor 0.8.5 — Word export quality

Both independent NuGet packages and the sample package references are updated to 0.8.5. No new package dependencies or hosting requirements.

## Word output improvements

- Every exported table includes `w:tblGrid` with nonzero column widths derived from the printable page width and available percentage hints. A merged title row does not dictate individual column widths. Horizontal merges remain editable.
- Only contiguous initial all-`th` rows repeat; the existing row-split policy remains. Ragged rows record omitted trailing grid columns. Empty tables have a valid empty cell.
- Direct paragraphs in cells retain separate editable paragraphs; nested table content is retained as inline content once rather than duplicated into the outer table. Full nested table layout and vertical merges are not supported in this release.
- Image dimensions are constrained to printable page and cell width/height while preserving proportions as closely as integer pixel rounding allows. Native images and alternative text remain.
- Word-specific explicit integer point font sizes are supported in addition to existing pixel sizes. Run/paragraph properties are inserted in schema-aware order.

## Usage

```csharp
var options = new EditorExportOptions
{
    FileName = "Operations-Report",
    PaperSize = "Letter",
    Orientation = "portrait",
    Margin = "normal",
    WordLayout = EditorWordLayout.Compact
};
var output = await exporter.ExportAsync(html, EditorExportFormat.Docx, options);
```

| Profile | Body size | Table size | Cell padding |
| --- | --- | --- | --- |
| Standard (default) | Previous implicit Word default | Previous implicit Word default | Previous implicit Word default |
| Compact | 10 pt | 9 pt | 3 CSS px |
| Comfortable | 14 pt | 12 pt | 8 CSS px |

`WordFontSize` and `WordTableFontSize` accept 8–36 points; `WordTableCellPadding` accepts 0–24 CSS pixels. Null values use profile defaults. Explicit supported HTML font sizes take precedence, including existing heading and code styles. Compact also sets single-line spacing and zero paragraph before/after spacing inside table cells. Actual pagination is calculated by the Word application and depends on fonts, paper, text, and layout. Grid widths are preferred widths; Word may adjust auto-fit tables to fit content. Existing explicit fixed table layout is respected. No Word page-number footer has been added; `ShowPageNumbers` remains PDF-only.

Standard preserves prior implicit font and padding, but the structural table and image corrections intentionally apply to all Word profiles, so existing Word pagination may change. No changes to shared HTML parsing/normalization, editor UI engine, PDF/XLSX exporters or browser setup. No changes to source HTML or saved data.

## Demo and verification

Run Azunt.Web and open `/editor/export`. Select Sample Report, Multi-page Report, or Images & Signature. Set the independent Word profile and download DOCX. Compare all three profiles in Microsoft Word, including column widths, repeated headers, merged heading, logo, signature, and page layout. Check that Word opens without a repair warning. Export the same document to PDF and Excel to check format isolation.

```powershell
dotnet build Azunt.Editor.sln -c Release
$env:AZUNT_TEST_WORD_OUTPUT = "$PWD\word-test-output"
dotnet run --project tests/Azunt.Editor.Export.SmokeTests -c Release
# Optional with a configured Playwright browser:
$env:AZUNT_TEST_PDF = "1"
dotnet run --project tests/Azunt.Editor.Export.SmokeTests -c Release
python tests/verify-release-contracts.py
python tests/verify-package-policy.py
python tests/verify-css-class-namespace.py
```

New smoke tests check actual DOCX grid widths, horizontal spans, repeated headers, independent cell paragraphs, explicit point sizes, profile overrides, invalid arguments, format isolation and OpenXmlValidator results. Image assertions accept both SDK-generated `media/` and `word/media/` part locations. These .NET tests were added but could not be executed in the patch preparation environment (no .NET SDK). Microsoft Word rendering, NuGet pack and package installation remain release gates. Existing Chromium print CSS tests passed, as did static release/package/CSS checks. No claim of actual 0.8.5 Word output validation is made until the .NET/Word tests run.
