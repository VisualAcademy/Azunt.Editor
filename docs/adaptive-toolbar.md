# Toolbar

Azunt.Editor uses the same JavaScript and CSS for MVC, Razor Pages and Blazor Server.

## Layout

`Compact` is the default. It displays a menu row and a quick toolbar row. Other tools are available through the menus and the `More` button. Selecting an image or table opens a small contextual toolbar.

`Classic` retains the full grouped toolbar.

MVC and Razor Pages:

```razor
<azunt-editor name="Html" toolbar-mode="Classic" />
```

Blazor Server:

```razor
<AzuntEditor @bind-Value="Html" ToolbarMode="Classic" />
```

Switch an active editor without changing the document:

```js
AzuntEditor.setToolbarMode("editor-id", "Compact");
AzuntEditor.setToolbarMode("editor-id", "Classic");
```

## One-row note toolbar (ShowMenuBar)

Use `ShowMenuBar=false` for short notes, project descriptions, comments, and modal
forms. The default is `true`, so existing editors are unchanged. This option
hides **only** the top File/Edit/View/etc. menu row in `Compact` mode. Undo,
Redo, formatting, **More**, Fullscreen, custom quick commands, contextual tools,
and the HTML data format remain available. `Classic` already has no menu row;
`ShowMenuBar` has no visual effect on that layout.

MVC / Razor Pages:

```html
<azunt-editor name="Description" height="180" toolbar-mode="Compact" show-menu-bar="false" />
```

Blazor Server:

```razor
<AzuntEditor @bind-Value="Description" Height="180" ToolbarMode="Compact" ShowMenuBar="false" />
```

Blazor parameters may change dynamically without replacing the live editor DOM,
or invoke `await editor.SetShowMenuBarAsync(false)` after the component is mounted.
JavaScript hosts can call `AzuntEditor.setShowMenuBar("editor-id", false)` and
`AzuntEditor.setShowMenuBar("editor-id", true)`. In one-row mode, `Alt+F10`
focuses **More** instead of a hidden menu heading.

Demo routes: `/editor/mvc?toolbar=note`, `/editor/razor-pages?toolbar=note`,
and `/editor/blazor?toolbar=note`. The Blazor demo includes a runtime checkbox.

## Custom commands

Register a command for one editor (or omit the second argument to register globally):

```js
AzuntEditor.registerCommand({
    id: "downloadPdf",
    label: "Download PDF",
    group: "export",
    placement: "menu",
    execute: ({ editor, getHtml, getPageSettings }) => {
        // Call your own export endpoint.
    }
}, "editor-id");
```

`placement: "toolbar"` pins the command in the quick row. Commands in the `export` group appear under Export; other supported groups are `file`, `edit`, `view`, `insert`, `format`, `table` and `tools`.

For Blazor Server, define `CustomCommands` and handle `CustomCommandInvoked`:

```razor
<AzuntEditor @bind-Value="Html"
             CustomCommands="Commands"
             CustomCommandInvoked="OnCommand" />
```

The component sends the command ID to the handler. Retrieve document HTML with `GetHtmlAsync()` if needed; this keeps large inline images on the established streaming path. Avoid passing Base64 HTML as a button callback argument.

## Notes

- Menu commands use the existing editing implementation; editor HTML is unchanged by layout switching.
- In the demo, Export menu actions invoke the existing MVC/Razor Pages form handler or Blazor export service. They are not part of the editor library's business logic.
- Classic can be retained in existing applications by setting `ToolbarMode` or `toolbar-mode` explicitly.


## Keyboard navigation

Compact mode supports keyboard access without requiring a mouse:

- `Alt+F10` while editing focuses the first menu.
- `Left` / `Right` move between menu headings; `Home` / `End` move to the first or last.
- `Down` / `Up` open the highlighted menu and focus its first or last command.
- Inside a menu, `Down` / `Up` move between command buttons. Inputs and selects retain their native keyboard behavior.
- `Enter` / `Space` activate the focused button, and `Escape` closes a menu and returns focus to its opener.
- `Tab` keeps normal browser focus order and closes an open menu when focus leaves the toolbar.

The current text style is reflected by highlighted Bold, Italic, Underline and other formatting controls and by the footer style label. Keyboard and mouse users share the same editing commands. Classic mode is unchanged.

## Responsive controls (0.6.2)

Compact's menu and contextual tools reposition inside the visible viewport when the
editor, page, or viewport is resized or scrolled. If there is insufficient space
below the anchor, the menu opens upward. On touch devices, buttons have a larger
minimum hit target. At very narrow widths the History quick group may be hidden;
Undo and Redo remain available from File and More menus.

At the caret, the footer shows explicitly applied font, pixel size, text color,
and highlight color as well as bold/italic/list state. The Format menu's controls
reflect supported inline formatting of the current selection. Nonstandard fonts
and sizes are not rewritten or removed from the document.

Use `Escape` to return focus from a menu to its opener. Tab and native select
keys retain browser behavior. OS dark appearance and forced-colors/high-contrast
settings have contrast-aware toolbar styles.
