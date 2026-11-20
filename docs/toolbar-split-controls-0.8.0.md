# Azunt.Editor 0.8.0 — Toolbar split controls layout

## Overview

The Compact toolbar now groups the Table, HTML form control, and Code buttons
inside the **Insert** group. Each uses one visually connected split button:

| Primary click | Adjacent dropdown |
| --- | --- |
| Table: insert a 2 × 2 table | Select 1–10 rows and 1–10 columns in the table grid |
| Checkbox: insert a checked checkbox | Select checked/unchecked checkbox or radio button |
| Code: open the C# code editor | Choose the code language |

Both halves share the same height, outer border, and interior dividing line.
In Classic mode the same controls remain available in their existing Table,
Form controls, and Writing tools groups, respectively.

At narrow widths the optional Compact Insert group may be hidden. The top-level
**Insert** menu still provides table, form-control and code insertion choices.
These menus retain existing keyboard navigation and focus restoration.

## Compatibility

- Existing `data-command`, `data-quick-command`, `data-azunt-menu`,
  `aria-expanded`, and menu dispatch behavior are preserved.
- The table primary button still inserts a 2 × 2 table; the table dropdown
  still opens the size picker directly beneath its trigger when space permits.
- The form primary button still inserts a **checked** checkbox.
- The code primary button still opens the C# code editor.
- All CSS classes retain the `az-` namespace. No JavaScript logic or public API
  or document HTML format has changed.
- Styles are scoped to the editor toolbar, and include dark/forced-color and
  coarse pointer handling.

## Validation

Run the dedicated layout and command regression:

```bash
python tests/editor-toolbar-split-080-browser.py
```

Then run existing picker, form, code, table grips, keyboard, Edit/View and
Export menu browser tests. In an actual .NET host verify both modes and hard
refresh the browser after deploying updated static assets.

The new Chromium test uses a DOM fixture matched to the shared markup source
and asserts source-level registration, not a running .NET application.
A Release build and MVC/Razor Pages/Blazor Server host smoke test are required
before releasing a NuGet package.
