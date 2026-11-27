# Azunt.Editor 0.8.9 — Document Styles

- Added opt-in Compact Report (13px body, 12px table), Business Document and Minimal content styles.
- Added optional style selector to Format, More and Classic toolbar; defaults remain Standard and selector disabled for existing hosts.
- Blazor binding and JavaScript event/getter expose selected style for host persistence. Style changes leave sanitized HTML and undo history untouched.
- New styles use a light document canvas and sans-serif fonts. Existing Browser Default and Standard rules are unchanged.
- Blazor demo starts in Compact Report; MVC and Razor Pages demos enable the selector.
- No changes to export engines, Print/Preview, sanitizer, or dependencies. Both packages version 0.8.9.

Limitations: styles are not embedded in HTML. Hosts must save the style separately. CSS is not fully isolated from host styles. Shared rendering for Print/Preview/PDF and custom document styles remain future work. Existing sanitizer does not preserve every arbitrary CSS property (e.g. cell padding).

Testing: run `python tests/editor-document-styles-089-browser.py` and `python tests/editor-native-rendering-088-browser.py` with Playwright/Chromium. Run the .NET solution and smoke tests to verify actual Razor/Blazor integration.
