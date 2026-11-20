# Azunt.Editor 0.7.2

## Improvements

- Adds ephemeral code block line numbers and a horizontal-scroll-safe, sticky gutter.
- Adds Enter auto indentation plus Tab and Shift+Tab indentation and outdent in the code editor.
- Improves C#, JavaScript, SQL, HTML, and CSS display-only syntax token coloring.
- Improves code block mobile sizing and print layout for long source lines.
- Updates Playwright PDF template to wrap long code lines and allow page breaks.
- Adds Chromium code block UX regression coverage.

## Compatibility and limitations

- No external JS editor/highlighter dependencies.
- Existing public APIs, stored HTML, and custom commands remain unchanged.
- Line numbers and syntax-color spans are client-side UI only, never stored or exported.
- Lines > 5,000 display without a numbered gutter.
- Complete .NET 10 Release build, real application host tests, and actual PDF/Word/Excel file generation require local validation.
