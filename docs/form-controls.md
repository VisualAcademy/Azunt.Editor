# Azunt.Editor 0.6.7 — HTML form controls

The shared toolbar contains a compact split control: **☐** inserts an unchecked checkbox; **▾** opens four options:

| Menu item | Stored HTML |
|---|---|
| Radio button (unchecked) | `<input type="radio">` |
| Radio button (checked) | `<input type="radio" checked>` |
| Checkbox (unchecked) | `<input type="checkbox">` |
| Checkbox (checked) | `<input type="checkbox" checked>` |

The same insertion commands are exposed in the **Insert** menu. The controls use the existing editing and document APIs; MVC, Razor Pages and Blazor Server need no API changes.

- Edit mode permits native checkbox/radio clicks; the current checked state is saved. Undo/Redo treats the control state as HTML.
- View mode disables controls, including server-rendered View content without JavaScript.
- Only `type=checkbox|radio` and the boolean `checked` attribute are allowed by the browser editor, View and Export sanitizers. Text/password/file/submit controls, names, values, form actions, inline events, and other attributes are removed. Never trust incoming HTML without host-side validation and access control.
- Generated radio buttons have no `name`, so they are independent indicators rather than mutually-exclusive form groups. This feature inserts document content, not a data-entry form system.
- Word and Excel export represent these as Unicode symbols rather than interactive controls; PDF/print uses native browser rendering.
- If a host previously stored HTML with unsupported `<input>` elements, they remain intentionally rejected. Existing supported HTML is unchanged.

## Local verification

```powershell
dotnet build Azunt.Editor.sln -c Release
dotnet run --project tests/Azunt.Editor.SmokeTests -c Release
dotnet run --project tests/Azunt.Editor.Export.SmokeTests -c Release
python tests/editor-form-controls-browser.py
```

Browser test exercises split-menu insertion, all four variants, checked state persistence, Edit/View transitions, input sanitization, independent editor instances and CSS dark mode.
