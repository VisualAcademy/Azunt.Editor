# Public API

Azunt.Editor uses the same JavaScript/CSS engine for MVC, Razor Pages and Blazor Server.

## Editor modes

`Edit` remains the default. `ReadOnly` is preserved for backwards compatibility:
it disables direct content editing but does not hide the toolbar. `View` displays
sanitized saved HTML as a read-only document, without toolbar, editing overlays,
status bar or source editor. No new JavaScript editor dependency is introduced.

MVC / Razor Pages:

```html
<azunt-editor id="article" name="Html" value="@Model.Html" mode="View" />
```

Blazor Server (add `@using Azunt.Editor.Models`):

```razor
<AzuntEditor Value="@HtmlContent" Mode="EditorMode.View" />
```

Blazor also supports changing the bound `Mode` parameter, or explicitly calling
`await editor.SetModeAsync(EditorMode.View)` and `SetModeAsync(EditorMode.Edit)`.
JavaScript hosts can call `AzuntEditor.setMode("article", "View")`.
Mode transitions preserve the current document, including pending HTML source
edits, without replacing the editor root. In View mode `setHtml` / `GetHtmlAsync`
remain available; changes are made programmatically, not by typing. The hidden
textarea is disabled for HTML form submission while View is active.

**Rendering and security (0.6.6):** View HTML is now rendered on the server
through `EditorViewerSanitizer` before JavaScript initializes. MVC/Razor Pages
can show the document with JavaScript disabled; Blazor prerendering/Static SSR
can show it before a SignalR connection is available. The full Edit/View
component still includes the editor DOM so it can switch modes interactively;
with JavaScript disabled the View is display-only and cannot switch modes.

Only supported tags and safe attributes/styles/URLs are emitted as HTML, and
text is encoded. The client continues to sanitize on mount. For manually
rendering viewer fragments, use `EditorViewerSanitizer.Sanitize(html)`, **not**
`new MarkupString(html)` on untrusted input. The built-in sanitizer is tailored
to Azunt.Editor HTML, not arbitrary webpage HTML, and not an authentication
or authorization boundary. Hosts must also validate on save, authorize stored
content and image access, and apply CSP as appropriate. Rendering a remote
image URL can contact an external server from the visitor's browser.

`Azunt.Editor.Export.Html.EditorHtmlSanitizer` remains the stricter export/storage
sanitizer when using the optional Export package; its default policy disallows
external images. Store and viewer policy should be chosen deliberately.

## Toolbar

Compact is the default. Set `toolbar-mode="Classic"` on `<azunt-editor>` in MVC/Razor Pages, or `ToolbarMode="Classic"` on `<AzuntEditor>` in Blazor Server.

On an active Blazor component, call `await editor.SetToolbarModeAsync("Classic")`. Toolbar changes do not replace document HTML.

## Custom commands

Register a JavaScript command for one editor:

```javascript
AzuntEditor.registerCommand({
  id: "downloadPdf",
  label: "Download PDF",
  group: "Export",
  placement: "Menu",
  execute: ({ getHtml, getPageSettings }) => {
    // Send the document to the host application.
  }
}, "editor-id");
```

Use `placement: "Toolbar"` for a quick-toolbar button. The optional `icon` field is short text, not HTML. `requiresEdit: true` disables a command in read-only editors.

Use `AzuntEditor.unregisterCommand("downloadPdf", "editor-id")` to remove a scoped command. Omitting the editor ID removes a global registration. Commands are removed from the UI immediately.

For Blazor Server, pass `CustomCommands` and handle `CustomCommandInvoked`. Each item supports `Id`, `Label`, `Group`, `Placement`, `Icon`, and `RequiresEdit`. After the editor mounts, replace the command list with:

```csharp
await editor.SetCustomCommandsAsync(commands);
```

Setting the list replaces only Blazor-provided commands. It does not remove JavaScript commands. Invalid or duplicated IDs, unsupported groups, or unsupported placements are rejected without changing the existing Blazor command list.

## Document data

- Blazor: `GetHtmlAsync()`, `SetHtmlAsync(html)`, `GetPageSettingsAsync()`.
- JavaScript: `AzuntEditor.getHtml(id)`, `setHtml(id, html)`, `getPageSettings(id)`.
- MVC/Razor Pages: use standard form posting with the configured `name` attribute.
- Use the existing HTML streaming path in Blazor for large inline images; do not send HTML in custom command IDs.

## Security and hosting

The host application is responsible for authorization, document/image storage, and server-side HTML validation. Custom commands execute host code and should not be treated as a security boundary. No third-party editor runtime or CDN is required.
