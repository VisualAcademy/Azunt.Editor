# Azunt.Editor 0.7.1

## Added

- Compact/Classic **Code** insertion button and language dropdown.
- Code textarea with language selection and controlled editing of saved blocks.
- Presentation-only token highlighting for C#, JavaScript, SQL, HTML, CSS.
- Code copy action in Edit and View modes; edit action only in Edit.
- Narrow code language metadata and safely encoded source text across browser,
  MVC/Razor Pages/Blazor server-side View, and Export sanitizers.
- Monospaced PDF export layout for code blocks.
- Chromium and .NET console regression checks for code roundtrips and XSS safety.

## Compatibility

- Existing HTML without `data-language` remains unchanged.
- Existing public editor APIs and Export command registration are unchanged.
- No new NuGet dependencies or external JavaScript library.

## Verification limits

- Browser and static validation can run in the patch build environment.
- .NET 10 Release build and real MVC/Razor Pages/Blazor hosting require local SDK.
- Syntax token colors are not currently generated inside DOCX/XLSX/PDF exports.
