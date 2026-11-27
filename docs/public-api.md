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

For one-row note editing, set `show-menu-bar="false"` on the MVC/Razor Pages
Tag Helper or `ShowMenuBar="false"` on the Blazor component, while retaining
`Compact`. The default is `true` for compatibility. This hides the top menu
row, not the **More** command menu. Live updates are available through
`SetShowMenuBarAsync(bool)` (Blazor) or `AzuntEditor.setShowMenuBar(id, bool)`
(JavaScript); no content reset is performed.

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

## Selection-aware extensions (0.8.7)

Capture a selection before opening a host-owned dialog (image library, approval,
quick note, signature picker, etc.). The bookmark is opaque, local to one editor
instance, and single-use. A newer capture replaces the previous one. Insertion
replaces the saved selection or inserts at the saved caret. No fallback to the
end of the document is attempted. Close the modal before restoring/inserting.

```javascript
AzuntEditor.registerCommand({
  id: "pickImage", label: "Image library", group: "Insert",
  placement: "Toolbar", icon: "Image", captureSelection: true,
  execute: async ({ editorId, selectionToken }) => {
    if (!selectionToken) return; // select an editable location first
    try {
      // Host function: returns an authorized image URL, or null on cancel.
      // It must close its dialog before returning.
      const url = await chooseImage();
      if (url && !AzuntEditor.insertImage(editorId, selectionToken, url, "Image", 240))
        alert("Select an insertion location and try again.");
    } finally {
      AzuntEditor.releaseSelection(editorId, selectionToken);
    }
  }
}, "editor-id");
```

Without a toolbar command, call `saveSelection(id)` synchronously before focusing
an external control. It returns a string or null; it does not invent a caret.
The remaining methods return booleans:

| JavaScript | Blazor component method |
| --- | --- |
| `saveSelection(id)` | `SaveSelectionAsync()` |
| `restoreSelection(id, token)` | `RestoreSelectionAsync(token)` |
| `releaseSelection(id, token)` | `ReleaseSelectionAsync(token)` |
| `insertHtml(id, token, html)` | `InsertHtmlAsync(token, html)` |
| `insertText(id, token, text)` | `InsertTextAsync(token, text)` |
| `insertImage(id, token, src, alt, width)` | `InsertImageAsync(token, src, alt, width)` |

`restoreSelection` consumes the token, so use an insert method directly when
inserting content. The insert methods restore the selection internally. The
existing paste engine handles the insertion, records Undo/Redo, and synchronizes
HTML to the hidden form field and Blazor binding. Cancel with `releaseSelection`.
An unsuccessful insertion never intentionally falls back to another position;
reselect and retry. Tokens are invalidated by document edits, Undo/Redo,
source mode, SetHtml, View mode, remount or unmount. Focus changes alone do not
invalidate a token. A missing/invalid selection, read-only state, active pointer
editing or IME composition prevents capture. CaptureSelection implies RequiresEdit.
The guard is deliberately conservative and can reject changes that only appear
visually identical. One bookmark per mounted editor bounds memory use.

### Blazor Server

Add `CaptureSelection = true` to the command definition and handle the new
`CustomCommandWithSelectionInvoked` event (`EditorCommandContext` contains
`CommandId` and nullable `SelectionToken`). The browser captures the token before
sending the callback to .NET. Existing commands still use `CustomCommandInvoked`.
If the new callback has no delegate, the old callback is invoked and the token
is released. No changes are needed for existing Export commands.

```razor
<AzuntEditor @ref="editor" CustomCommands="commands"
             CustomCommandWithSelectionInvoked="OnPickAsync" />
```

```csharp
private async Task OnPickAsync(EditorCommandContext context)
{
    if (editor is null || context.SelectionToken is not string token) return;
    try
    {
        var url = await ChooseAuthorizedImageAsync(); // host closes picker first
        if (url is not null && !await editor.InsertImageAsync(token, url, "Image"))
            notice = "Select a location and try again.";
    }
    finally { await editor.ReleaseSelectionAsync(token); }
}
```

Use a host-approved URL or supported PNG/JPEG/GIF/WebP data URL. Existing editor
sanitization and document/image limits apply (16 MiB document, 2 MiB per inline
image, 8 MiB aggregate inline images; the estimate conservatively includes the
existing selection). Text is escaped; HTML is sanitized using the existing
editor policy. This is not an authorization boundary or a server sanitizer.
Complex block fragments use the existing paste behavior, not a new document
layout engine. The host must authenticate the user and authorize the chosen
image/signature, validate HTML on save, and enforce its own storage policy.
An image insertion does not certify or cryptographically sign a document.

## Content rendering (0.8.8)

`EditorContentRendering.Standard` preserves the existing editor presentation.
`EditorContentRendering.BrowserDefault` uses browser-like typography, paragraph
spacing and natural table widths for sanitized HTML. Set `ContentRendering` on
the Blazor component, `content-rendering` on the Tag Helper, or use the new
`EditorMarkup.Render` overload with `contentRendering` as its final argument.

After mounting, `AzuntEditor.setContentRendering(id, "BrowserDefault")` or
`await editor.SetContentRenderingAsync(EditorContentRendering.BrowserDefault)`
changes the view without editing the HTML. Use `Standard` to return. This is
a host presentation option, not a saved document style. The same setting must
be supplied when reopening in Edit or View. Existing Preview, Print and export
rendering are unchanged. The HTML allowlist is unchanged; arbitrary CSS, legacy
HTML attributes and external style sheets are not newly supported. Fixed-layout
tables retain full canvas width to support the existing column-resize contract.

## Document style selector (0.8.9)

`EditorContentRendering` adds `CompactReport`, `BusinessDocument` and `Minimal`.
Existing values and overloads remain unchanged. The selector is disabled by
default; enable `ShowDocumentStyleSelector` (Blazor, initial markup only), or
`show-document-style-selector` (Tag Helper). It is in Format and More for
Compact toolbars and directly in the Classic toolbar. View mode hides it.

Blazor: `@bind-ContentRendering` receives user selections; alternatively use
`ContentRenderingChanged` and persist the value in the host. Read the live
value via `GetContentRenderingAsync()`. MVC/Razor Pages: listen to the bubbling
`azunt:content-rendering-changed` event (`event.detail.rendering`) and use
`AzuntEditor.getContentRendering(id)`. Programmatic setters update the selector
without firing the user change event. No HTML, history, or form field is added
by a style change. A host must persist the enum separately; it is not embedded
in the saved HTML. The existing sanitizer still determines supported inline
styles. Print, Preview, PDF, Word, and Excel are unaffected in this release.


## Shared document styles (0.8.10)

No new host API or service registration is needed. The native `preview` and
`print` commands capture the current `ContentRendering` value when invoked.
The print window defaults to Match document and reuses the document rules from
the loaded Azunt.Editor CSS. Its layout selector can choose the three legacy
print-only profiles without changing the source editor or its HTML/history.
The print window does not follow subsequent changes in the originating editor.

Use the matching version of `azunt-editor.js` and `azunt-editor.css` from the
normal same-origin static assets. The stylesheet must be readable via CSSOM
(directly loaded or bundled as top-level rules); if missing or inaccessible,
Preview shows an explicit warning and Standard fallback, and direct Print
requires user confirmation via the print button instead of auto-printing.
Only built-in document rules are shared; host CSS is not copied. Printing uses
light colors and actual paper margins, and can wrap code and paginate tables.
Browser PDF output via Print / Save as PDF is covered; server PDF, DOCX and XLSX
remain independent. Persist `ContentRendering` separately if needed.

## Custom document styles (0.8.11)

Blazor: supply `CustomDocumentStyles` (an `IReadOnlyList<EditorDocumentStyle>`) and optionally bind `DocumentStyleId` to a built-in enum name or `custom:<id>`. The list is captured at initialization; recreate the component to change the list. `ShowDocumentStyleSelector` enables the menu. `DocumentStyleId` takes precedence over `ContentRendering`; do not bind both as competing settings. `SetDocumentStyleAsync` and `GetDocumentStyleIdAsync` provide imperative access. `ContentRendering` remains the fallback built-in style.

MVC/Razor Pages: supply `custom-document-styles="@Model.Styles"` and `document-style-id="custom:office-report"` to the tag helper.

JavaScript: `AzuntEditor.registerDocumentStyles(id, styles)` replaces the per-instance list; `AzuntEditor.setDocumentStyle(id, "custom:office-report")` selects a preset and `getDocumentStyleId(id)` reads it. Use camelCase property names. The `azunt:document-style-changed` event fires for menu selections.

Up to 20 styles, unique lowercase ASCII ids (letters/digits/hyphens, starting with a letter), labels up to 80 characters, font families System/Arial/Georgia/TimesNewRoman/Consolas, integer font sizes 8–32 CSS px, line height 1–2.5, document padding 0–60 px, cell padding 0–24 px, and six-digit hex colors. No arbitrary CSS. Style changes do not alter HTML; hosts must persist the style ID separately. Browser Preview/Print Match document uses the selected style. Server PDF/Word/Excel export is not automatically styled.
