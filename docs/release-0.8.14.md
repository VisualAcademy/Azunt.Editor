# 0.8.14 — Common export integration

- Optional Blazor `AzuntExportIntegration` with common format dispatch, live HTML
  and per-request option callbacks, duplicate request suppression, status and
  streamed file download. Old buttons and direct export service callers remain.
- `EditorExportMenu` supplies shared IDs, labels, format flags and strict form
  format parsing without coupling the two packages.
- MVC/Razor Pages can opt in to the existing form's export buttons from the
  editor Export menu; no new controller endpoint, database or dependency.
- Blazor demo uses the shared component and keeps the 0.8.13 Match document
  options for PDF and Word, including custom style mapping. MVC/Razor demo uses
  its existing export options and antiforgery-protected POST handler.
- The new integration does not change the document converters, stored HTML,
  custom command handling in the core editor, or default export settings.

See `docs/public-api.md` for the integration example. Format flags restrict UI
and the Blazor dispatcher, not access control on a host endpoint. Export script
and styles are still required. Toolbar registration is host opt-in. Browser form
submission uses normal navigation/download behavior rather than the Blazor
status component. Host-specific permission, image resolution, filename, and
output options remain the host's responsibility.

Verification: `python tests/editor-export-integration-0814-browser.py`,
`python tests/verify-release-contracts.py`, `python tests/verify-package-policy.py`,
`dotnet run --project tests/Azunt.Editor.Export.SmokeTests`, and run Azunt.Web to
verify Blazor streaming and MVC/Razor Pages form downloads in the actual host.
