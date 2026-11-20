# Azunt.Editor

A lightweight rich-text editor for ASP.NET Core MVC, Razor Pages, and Blazor Server.

Version: **0.6.6** · .NET 10

## Projects
- `src/Azunt.Editor` — shared editor components, JavaScript, and CSS
- `src/Azunt.Editor.Export` — PDF, Word, and Excel export
- `samples/Azunt.Web` — MVC, Razor Pages, and Blazor Server examples
- `tests` — smoke and browser tests

## Build
```powershell
dotnet restore Azunt.Editor.sln
dotnet build Azunt.Editor.sln -c Release
dotnet run --project samples/Azunt.Web
```

## Edit and View modes

The default `Edit` mode keeps the current editor behavior. Set `mode="View"` in
MVC/Razor Pages or `Mode="EditorMode.View"` in Blazor to display the same
sanitized document without editing tools. `ReadOnly` remains a separate,
backwards-compatible option. See [public API](docs/public-api.md) for examples,
mode transitions and server-rendering security guidance.

Demo: `/editor/mvc?mode=view`, `/editor/razor-pages?mode=view`, or toggle the
mode on `/editor/blazor` without discarding the current document. Start in View
using `/editor/blazor?mode=view`. In 0.6.6,
View content is emitted server-side through a restricted HTML allowlist, so
initial viewing does not depend on the editor JavaScript or a SignalR circuit.

## Images

The default editing mode uses inline Base64. The host may also store images
when inserted or convert them into short image URLs when the document is saved.

| Demo mode | Blazor URL | Behavior |
| --- | --- | --- |
| Inline | `/editor/blazor` | Keep Base64 in HTML |
| Stored | `/editor/blazor?images=stored` | Upload images when inserted |
| Save | `/editor/blazor?images=save` | Convert Base64 to stored URLs on Save |

The same `images` query parameter works on `/editor/mvc` and
`/editor/razor-pages`. File-based demo storage is available only on local
Development environments. Documents are stored in the demo session/component;
the application must implement permanent document storage.

Image files are stored outside `wwwroot` in `samples/Azunt.Web/App_Data/editor-images`.
The local demo serves immutable image IDs with browser-private caching. In a
production application, the host must implement authentication, document
ownership checks, storage, and an appropriate cache policy. Azure Blob Storage
can be used behind authenticated host URLs; do not embed private SAS tokens in
saved HTML.

For integration details, see [image persistence](docs/image-persistence.md).

## License
MIT. See [LICENSE](LICENSE).

## Toolbar

Compact mode (two rows) is enabled by default. Classic mode keeps the full grouped toolbar.
Both modes use the same commands, document HTML and image handling.

MVC / Razor Pages: `<azunt-editor toolbar-mode="Classic" name="Html" />`

Blazor Server: `<AzuntEditor ToolbarMode="Classic" @bind-Value="Html" />`

Custom buttons can be registered by the hosting application, including PDF, Word and Excel commands.
See [toolbar configuration](docs/adaptive-toolbar.md).

## Editor status messages

Success notices disappear after 3 seconds, information after 4 seconds, and
warnings after 5 seconds. Errors stay visible until another status replaces
them. These messages are not part of the document HTML.

Host applications can report a status with `AzuntEditor.showStatus(editorId,
"Saved.", "success")`. The supported levels are `success`, `info`, `warning`,
and `error`. Passing an empty message clears the status.

## Keyboard access

In Compact mode, press `Alt+F10` to focus the menu bar. Use the arrow keys to
navigate menus, `Enter` to activate an item, and `Esc` to return focus.
Active text formatting is shown on the toolbar and in the editor footer.

## Custom commands

Host applications can add commands to a single editor or globally. Commands can be removed
with `AzuntEditor.unregisterCommand(id, editorId)`. Blazor Server can replace the registered
command list after mounting with `SetCustomCommandsAsync(commands)`; full HTML remains on
the existing streaming path. See [public API](docs/public-api.md).

## Toolbar layout

In Compact mode, the More menu uses a width-limited grid. The layout is
shared by MVC, Razor Pages and Blazor Server, including wide host forms.

### CSS namespace (`az-` prefix)

Starting with **0.6.3**, all CSS class names owned by `Azunt.Editor` and
`Azunt.Editor.Export` begin with `az-`. Examples:

| Previous (0.6.2) | Current (0.6.3) |
| --- | --- |
| `.azunt-editor` | `.az-azunt-editor` |
| `.azunt-editor-toolbar` | `.az-azunt-editor-toolbar` |
| `.azunt-editor-canvas` | `.az-azunt-editor-canvas` |
| `.azunt-editor-export-actions` | `.az-azunt-editor-export-actions` |
| `.azunt-print-page` | `.az-azunt-print-page` |

Custom CSS and JavaScript selectors written by the **host application** must be
updated to use the new classes. Editor `data-*` attributes, the `<azunt-editor>`
Tag Helper, `AzuntEditor` public JavaScript API, and CSS/JS asset filenames remain
unchanged. The editor's saved document HTML is not rewritten by this naming change.
The namespace is an identification convention, **not a change of license**: MIT.

For the complete migration notes, see `docs/css-class-namespace.md`.

### Windows ARM64 and dark appearance

For PDF browser setup on Windows ARM64 and troubleshooting light/dark editor controls,
see [Windows ARM64 and dark-mode guidance](docs/windows-arm64-and-dark-mode.md).

