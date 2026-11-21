# Azunt.Editor 0.8.1 — Document export stabilization

This release is based on **0.8.0 including the More menu layout hotfix**. It changes the export service only; the editor's stored HTML, CSS/JS toolbar behavior, component APIs and NuGet dependency versions are unchanged.

## Export fixes

- **Word:** Use export-only lexical token colors for supported C#, JavaScript, SQL, HTML and CSS code blocks. Source code stays plain, safe text; line breaks and tab runs are preserved. Colorization is deliberately lightweight, not a complete compiler grammar.
- **Word links:** Avoid a malformed external hyperlink relationship when the editor contains a valid **relative** or fragment link. Preserve its visible label, while preserving normal clickable absolute `https:`, `http:` and `mailto:` links.
- **Word images:** Preserve aspect ratio for tiny raster images without forcing a 20px minimum height; scale very tall images down proportionally.
- **Excel:** Preserve UTF-16 surrogate pairs when a single code line exceeds the 32,767-character cell limit. Remove illegal XML controls and replace broken surrogate code units rather than generating malformed SpreadsheetML. Existing bounded, editable text-cell layout remains unchanged.
- **PDF:** Add export-only syntax token spans to the already sanitized HTML, using a bounded matcher and re-escaping every token. No JavaScript or remote image requests are enabled. Printable colors are defined in the PDF print stylesheet, without changing editor styles or saved HTML.

## Scope / known limitations

- This release does **not** change browser installation on Windows ARM64 or Azure: host configuration still decides the Playwright executable/channel. Reinstall the exact browser version where required. Production PDF failures should be diagnosed with the existing architecture/channel details in the inner exception.
- Print syntax coloring is approximate: five supported languages, keywords/comments/strings/numbers and HTML tag regions. Excel still exports code as editable **monochrome** rows.
- Relative links in Word are plain readable text. An exported document cannot reliably resolve the host application's base URL.
- PDF CSS preview is not the same as a successful `.NET` PDF export; DOCX/XLSX inspection requires the .NET 10 SDK.

## Recommended verification

1. `dotnet restore` and `dotnet build -c Release` on the actual solution.
2. `dotnet run --project tests/Azunt.Editor.Export.SmokeTests` for actual generated DOCX/XLSX structural assertions (including color runs, links, images and supplementary Unicode).
3. Install the matching Playwright browser and use `AZUNT_TEST_PDF=1 dotnet run --project tests/Azunt.Editor.Export.SmokeTests` to exercise real PDF output.
4. `python tests/editor-export-080-pdf-layout.py` and `python tests/editor-export-081-pdf-color-layout.py` for browser PDF layout tests, if Chromium/Python Playwright are available.
5. Verify export of the same document from MVC, Razor Pages and Blazor, and test on actual x64 and ARM64 hosts. Do not rely solely on static source checks.
