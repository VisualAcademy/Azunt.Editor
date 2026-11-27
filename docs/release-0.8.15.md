# 0.8.15 — Legacy HTML compatibility

## Scope

The first legacy compatibility pass targets report markup patterns found in the
host's Analytics, Incident and Malfunction report templates, together with
synthetic legacy HTML fixtures. There is no host-specific runtime dependency.

- Preserve named HTML font sizes (normalized to bounded integer pixels), integer
  pt sizes, the first recognized font family, a small fixed color palette,
  short hex/RGB colors, legacy `font` attributes (converted to `span`) and
  bounded inline font weight, italic and text decoration.
- Preserve flat Roman/alpha/decimal list styles and a positive start value
  from 1 to 9999. Word uses literal ordered-list labels, not native numbering.
- Recognize text alignment attributes and specific text-align utility classes,
  table percentage width attributes, bounded cell pixel widths, more precise
  percentage widths (rounded to three decimals), vertical cell alignment,
  no-wrap cells, zero borders and solid top borders for signature areas.
- Preserve bounded pixel image dimensions from inline CSS. Existing image URL,
  managed-image and server-side remote fetch policies are unchanged.
- Word recognizes explicit table/cell border removal, solid top borders,
  inline emphasis and flat ordered-list labels. PDF retains the supported
  markup through the existing sanitizer. Excel generation is unchanged.

## Migration safety

This is a selective allowlist extension, not a general HTML/CSS parser or a
byte-for-byte migration. Do not bulk overwrite historical documents. Keep the
original HTML and compare a working copy before deploying an edit/save workflow.
Arbitrary classes, CSS, script, event handlers and external stylesheet/font
loading are not added. Font availability and browser defaults vary. Unsupported
font fallback stacks, relative sizes, complex/nested Word lists, rowspan,
colgroup, arbitrary CSS and exact Word cell sizing remain out of scope. Existing
managed image integration must be used for server-side export; arbitrary remote
images are not fetched. No database, default editor profile, or public command
API changes.

## Verification

- `python tests/editor-legacy-html-0815-browser.py` — synthetic report fixture,
  sanitized round-trip, CSS values, edit/view and unsafe markup removal.
- `python tests/editor-paste-070-browser.py` — semantic clipboard conversion;
  direct HTML now intentionally preserves bounded inline emphasis.
- Existing table width, custom/shared styles and export integration regressions.
- `dotnet run --project tests/Azunt.Editor.SmokeTests`
- `dotnet run --project tests/Azunt.Editor.Export.SmokeTests`

The C# smoke tests and actual DOCX output must be run with the .NET SDK. Test
representative historical documents, including images, lists, tables and
signatures, on the target host before changing the default editor.
