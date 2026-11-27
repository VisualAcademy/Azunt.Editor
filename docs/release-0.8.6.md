# Azunt.Editor 0.8.6 — Print and document import

## Changes

- Correct paper dimensions for A4, Letter and Legal in browser Print Preview, including landscape.
- Print-only Standard (default), Compact and Comfortable density controls in the preview popup. The editor content and server export options are not modified. Existing direct Print still starts in Standard.
- Repeat semantic table headers, keep rows together where possible and wrap long code blocks when printing. Preview is continuous rather than an exact paginated view; the browser determines final pagination.
- Bounded image/font preparation with an explicit warning for missing images; automatic Print stops at preview if an image fails. Browser settings still control native headers, footers and PDF saving.
- Optional document import through the existing Export package, independent of editor hosting mode; import testing UI at `/editor/export`.
- Both packages versioned 0.8.6. No new dependency. No changes to the server PDF, Word or Excel engines or the existing sanitizers.

## Import API

```csharp
using Azunt.Editor.Export.Import;

var result = EditorDocumentImporter.Import(fileName, bytes);
// Present result.Warnings and ask the user before replacing the editor content.
await editor.SetHtmlAsync(result.Html);
```

For MVC/Razor Pages, return the result from an authenticated, antiforgery-protected upload endpoint and pass the returned HTML to `AzuntEditor.setHtml(id, html)` after user confirmation. Programmatic replacement resets undo history as before. The host is responsible for preserving the old document if undo across imports is needed, limiting requests, authorization, and validating before persistence.

### Supported scope

- UTF-8 HTML fragments/documents and plain text. Existing restrictive HTML sanitizer applies; external images are removed. Full-document CSS and unsupported markup are not reproduced.
- Transitional DOCX body paragraphs, common direct run formatting, Heading1–4 style names, paragraph alignment, simple tables, horizontal spans (up to 12 columns), explicit page breaks, supported embedded raster images and alt text. Link text is kept without Word link targets.
- Not a lossless Word round trip. Numbering, custom styles, vertical merges, page settings, headers/footers, tracked changes, fields, equations and complex objects may be flattened or omitted. Legacy `.doc`, `.docm`, PDF and XLSX import are not supported. Review before replacing existing content.
- 8 MiB upload, 64 MiB declared expanded ZIP size, up to 2,048 ZIP entries, 16 MiB XML part/character limit, bounded XML depth and node count; up to 32 raster images, 2 MiB per image and 6 MiB total. HTML output also follows the existing 12-million-character sanitizer limit. No extraction to disk, DTD processing or external relationship fetching. The library is not an antivirus or general-purpose HTML sanitizer.

## Verify

```powershell
dotnet build Azunt.Editor.sln -c Release
dotnet run --project tests/Azunt.Editor.Export.SmokeTests
python tests/editor-print-086-browser.py
python tests/verify-release-contracts.py
```

Test Print Preview from the existing MVC, Razor Pages and Blazor samples. Select Letter/Legal and landscape, switch profiles, print to PDF, verify repeated headers and page breaks. In Export Demo, select a sample `.docx`, cancel first, then import and edit a second time. Verify HTML and plain-text files, invalid/oversize rejection, and existing downloads. The browser test exercises the real JS with a fixture, not a compiled ASP.NET host.
