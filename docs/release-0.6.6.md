# Azunt.Editor 0.6.6 — View and Export stabilization

## Changes

- View mode renders a restricted HTML subset in the server response before client JavaScript or a Blazor Server circuit is available. Existing MVC, Razor Pages, and Blazor Edit/View APIs remain unchanged.
- Initial View markup preserves supported paragraphs, tables, links, images, formatting and code containers. Text, URLs, attributes and styles are sanitized before insertion.
- The JavaScript editor flushes pending `OnHtmlChanged` notifications before an Edit → View transition; mode switches preserve the current undo history and host-provided Export commands.
- Protocol-relative URLs and backslash/control-character URL forms are rejected by the client sanitizer.
- The Blazor demo can start directly in View mode at `/editor/blazor?mode=view`.

## Verification

From the solution root:

```powershell
dotnet restore Azunt.Editor.sln
dotnet build Azunt.Editor.sln -c Release
dotnet run --project tests/Azunt.Editor.SmokeTests -c Release
dotnet run --project tests/Azunt.Editor.Export.SmokeTests -c Release
dotnet run --project samples/Azunt.Web
```

Check these URLs in a browser and with JavaScript disabled:

- `/editor/mvc?mode=view`
- `/editor/razor-pages?mode=view`
- `/editor/blazor?mode=view` (the initial prerendered HTML is visible without SignalR)

In the interactive Blazor page, switch Edit → View → Edit repeatedly and confirm the Export menu still includes PDF, Word and Excel. Confirm that actual downloads complete separately; Export menu tests do not exercise Playwright-based PDF generation.

Browser automation (Python Playwright with Chromium):

```powershell
python tests/editor-066-view-export-browser.py
python tests/editor-view-mode-browser.py
python tests/editor-public-api-browser.py
```

**Note:** HTML allowlisting is tailored to the editor's supported document structure. Hosts still control authorization, save-time validation, image access, CSP and server-side export validation. SSR does not make interactive Edit/View switches work without JavaScript.
