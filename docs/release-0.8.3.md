# Azunt.Editor 0.8.3 — Excel export improvements

Both `Azunt.Editor` and `Azunt.Editor.Export` are versioned at 0.8.3. This release improves the common Excel exporter; the editor's saved HTML, toolbar and JavaScript are unchanged. Word and PDF exporters are unchanged.

## Excel export

- Native editable XLSX cells with improved header fill, bold text, thin borders and wrapping.
- Improved column widths, estimated row heights, horizontal merges and placement of embedded PNG/JPEG images within table rows.
- Opt-in native date and numeric cells using explicit first-header-row names. Identifiers and leading zeros remain text by default. Formula-like input is not evaluated.

```csharp
var options = new EditorExportOptions
{
    SpreadsheetDateColumns = new[] { "Open", "Close" },
    SpreadsheetNumberColumns = new[] { "Quantity" },
    SpreadsheetCulture = "en-US"
};
```

Conversion is restricted to named columns and recognized values. ISO dates are accepted; `en-US` additionally supports selected month/day/year and AM/PM formats. No timezone conversion is performed. Numeric conversion accepts bounded plain decimals without leading zeros; unrecognized values stay text.

## Compatibility and limits

- Existing export calls remain supported; typed columns are opt-in. Default spreadsheet formatting is improved even without these options.
- Horizontal merges are supported; vertical merges (`rowspan`), exact signature layout, arbitrary HTML CSS fidelity and separate data-only worksheets are not implemented in this release.
- Remote image fetching remains disabled. Existing bounded document limits apply; over-limit tables may fail explicitly rather than silently truncate.
- Target framework remains .NET 10 and dependency versions are unchanged.

## Before publishing

```powershell
dotnet restore Azunt.Editor.sln
dotnet build Azunt.Editor.sln -c Release
dotnet run --project tests/Azunt.Editor.Export.SmokeTests -c Release
dotnet pack src/Azunt.Editor/Azunt.Editor.csproj -c Release
dotnet pack src/Azunt.Editor.Export/Azunt.Editor.Export.csproj -c Release
```

Open generated XLSX files in Excel, verify row counts, date/numeric types, literal identifiers and images, and repeat existing Word/PDF and IRW/SAT integration checks. Package-mode samples require the 0.8.3 packages to be available in a configured NuGet feed. Default sample project references do not require those packages to be published.
