# Azunt.Editor

A lightweight rich-text editor for ASP.NET Core MVC, Razor Pages, and Blazor Server.

Version: **0.8.0** · .NET 10

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

## Fullscreen toolbar control (0.8.0 UI hotfix)

The Compact toolbar now keeps a visible **Fullscreen** (expand) button at the
far right, beside **More**. Click to expand only the editor to the available
viewport; click again or press **Esc** to return. Classic still offers the
original Fullscreen command in **Writing tools**. The command preserves HTML,
and Edit → View automatically exits fullscreen because View has no edit toolbar.
The feature uses CSS positioning, not the browser's F11 / Fullscreen API.
See [fullscreen toolbar notes](docs/fullscreen-toolbar-0.8.0.md).

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

## Code block editing improvements (0.7.2)

- Line numbers appear in Edit/View after JavaScript initialization without modifying saved HTML or copied code.
- Long lines scroll horizontally, with a sticky number gutter. For blocks over 5,000 lines, the gutter is omitted to protect performance.
- The code input supports Enter indentation, Tab indentation, Shift+Tab outdent and selecting multiple lines.
- Improved token colors for C#, JavaScript, SQL, HTML and CSS. Print and PDF output wraps long source lines.
- See `docs/code-blocks-072.md` for behavior, compatibility and test steps.

## Programming code blocks (0.7.2)

Use the **Code** split button in either toolbar (or the Insert menu) to add C#,
JavaScript, SQL, HTML, or CSS snippets. A dedicated textarea edits the literal
source code. Existing blocks can be edited by double-clicking or selecting Edit;
Copy copies the original source, not the syntax-coloring markup.

Stored HTML uses only `<pre class="az-code-block" data-language="csharp"><code>…</code></pre>`.
The code text is HTML-encoded and cannot execute. Color spans and Copy/Edit
buttons are **display-only** and are never saved to HTML or exported. View mode
shows the same code with syntax colors when JavaScript is enabled, while the
server-rendered initial document displays safe, monospaced code without JS.
PDF export has a readable monospaced style; Word and Excel preserve plain code
text, not exact highlighted colors. [Details and limitations](docs/code-blocks.md).

## Undo/Redo reliability (0.7.0 follow-up)

- Restore the caret and selected text when undoing/redoing changes to visual HTML.
- Group Korean/other IME composition updates into one history step.
- Route supported browser-native history events through the same history stack.
- Preserve form control states and Edit/View cycles with bounded history.

See [Undo/Redo reliability](docs/undo-redo-stabilization.md) for limitations and tests.

## HTML paste reliability (0.7.0)

Pasting from Word, Google Docs, websites or plain text uses the same safe HTML
allowlist as other editing operations. Clipboard fragment markers and document
headers are removed. A narrow subset of inline weight/italic/underline/strike
styles becomes semantic HTML without retaining Word's `mso-*` or other
unapproved CSS. Pasting paragraphs or tables into the middle of an existing
root-level paragraph splits the surrounding paragraph rather than nesting
block elements inside it; surrounding text is preserved. This change affects
clipboard paste/drop only; programmatic `setHtml` and document storage remain
backwards compatible. See [paste behavior](docs/html-paste.md).

## Table editing reliability (0.7.0 follow-up)

Merge/split, row/column changes and header toggle preserve the editing caret and
content. A right-hand cell containing only a checkbox, radio button or image is
no longer dropped by Merge Right. `Tab` / `Shift+Tab` move between existing table
cells; browser focus behavior remains unchanged at the table boundaries.
Rectangular multi-cell selection is not included.

See [Table editing stability](docs/table-editing-stabilization.md).

## Image and HTML roundtrip reliability (0.7.0 follow-up)

Async image reads and uploads now preserve the original insertion position even
if the user changes focus. Pending imports are canceled or ignored when the
editor changes mode, enters HTML source mode, unmounts, or receives a replacement document. Multi-image
insertion order and inline Base64 byte counting are stabilized. An interrupted
resize is rolled back before View serialization. No stored HTML or public API
changes were introduced. See [image persistence](docs/image-persistence.md).

## Table dimension picker (0.6.8)

In Compact and Classic toolbars, the table split button inserts a 2 × 2
HTML table when the grid icon is pressed (unchanged behavior). Press **▾**
to open a compact palette immediately below that button and choose from
**1–10 columns × 1–10 rows**. Hover/focus previews dimensions before insert.
Use arrow keys and Enter for keyboard insertion, or Escape to dismiss.
The palette stays in the visual viewport, including narrow screens, and the
HTML saved is the same ordinary `<table>` / `<tr>` / `<td>` structure.
Existing table row/column and merge/split tools remain available.

See [table picker](docs/table-picker.md) for details and limitations.

## HTML form control insertion (0.6.7)

The Compact and Classic toolbars include a split button: **☑** inserts a checked
checkbox, while **▾** opens four options: checked/unchecked checkboxes and
unchecked/checked radio buttons. The Insert menu exposes the same options.

The saved HTML uses only `<input type="checkbox">`, `<input type="radio">`,
and optional `checked`; generated inputs have no `name`, `value`, script or
host-form submission semantics. When clicked in Edit mode, the checked state
is synchronized with saved HTML. In View mode the controls are disabled and
retain their visual checked state. Word/Excel export uses corresponding Unicode
glyphs (☐, ☑, ○, ◉) rather than interactive form fields.

See [form controls](docs/form-controls.md) for usage and limitations.

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

In Compact mode, **More** matches the height of the other toolbar buttons
and opens a responsive grid across the visible width of the editor rather than
stopping at a fixed 860px. Wide hosts show more command groups per row; narrow
screens retain a scrollable, viewport-clipped menu. The table-size picker and
other small dropdowns keep their existing, compact behavior. The layout is
shared by MVC, Razor Pages and Blazor Server.
See [More menu layout](docs/more-menu-layout-0.8.0.md).

The Table, checked Checkbox and Code buttons are grouped under **Insert** in
Compact mode. Their adjacent dropdown arrows are part of the same split control:
select a table size, form-control variation or code language without changing
the primary button behavior. Classic mode uses the same control heights.
See [split toolbar controls](docs/toolbar-split-controls-0.8.0.md).

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


## Table movement and column resizing (0.7.0 follow-up)

Edit mode shows a drag grip at a selected table's upper-left edge so the whole table can be moved between document blocks. A vertical grip between adjacent columns resizes widths on simple, unmerged tables; size choices are stored as safe percentages and persist in View mode. Both are undoable; grips are not included in saved HTML. See [Table movement and column resizing](docs/table-movement-and-column-resizing.md).


## Export quality improvements (0.8.0)

PDF, Word and Excel output retain the same export service and public API. Word now preserves
code line breaks and tabs with monospace formatting; Excel stores long code as editable rows
with header/code styles and honors safe table-width hints. PDF layout has consistent print
margins and code backgrounds. See [0.8.0 release notes](docs/release-0.8.0.md).

Release builds and full Open XML/PDF generation must be validated with .NET 10 and the
appropriate Playwright browser on each production host.
