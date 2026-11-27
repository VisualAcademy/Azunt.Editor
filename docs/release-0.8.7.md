# Azunt.Editor 0.8.7 — Extension API

- Opt-in browser-captured selection tokens for host commands, with a dedicated Blazor context callback.
- Explicit capture, restore, release and safe HTML/text/image insertion methods.
- Reject stale, cross-editor, consumed and read-only bookmarks; no fallback insertion.
- Reuse existing paste, history, sanitizer and HTML synchronization paths.
- Add `/editor/extensions` sample with a host-owned native dialog and fictional signature image.
- Existing custom command callbacks, PDF/Word/Excel export and Print/Import remain unchanged.
- Keep both package versions aligned at 0.8.7; no new dependencies.

See [public API](public-api.md#selection-aware-extensions-087) for lifecycle and limits.
This is a generic insertion API, not an application-specific image store, user signature system or electronic signature service.

## Verification

Chromium fixture: `python tests/editor-extension-087-browser.py`. Covers modal focus, escaped text, sanitized HTML, image validation, Undo/Redo, stale token rejection, multiple editor isolation, and old/new Blazor callback routing. Also run existing browser regressions, `dotnet build Azunt.Editor.sln`, smoke tests and the real interactive demo before packing and publishing.

The existing multiple-editor fullscreen Escape assertion also fails against the unmodified 0.8.6 baseline in the test environment. Fullscreen behavior is not changed by this release. The actual .NET build and NuGet pack were not run in this environment.
