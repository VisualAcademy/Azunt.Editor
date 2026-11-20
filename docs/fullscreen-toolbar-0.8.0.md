# Fullscreen toolbar control (0.8.0 hotfix)

## Purpose

The fullscreen command existed in the Classic **Writing tools** group and
in the Compact menus, but Compact did not provide a visible shortcut. The
editor toolbar now includes a dedicated expand icon at the far right of the
Compact quick row; it is not subject to the optional-group collapse rules.

## Interaction

- Compact: click the expand icon next to **More**.
- Classic: use the existing Fullscreen button under **Writing tools**.
- Click the same control to exit, or press **Esc**. Focus returns to the
  fullscreen control, where possible.
- The fullscreen control reports `aria-pressed` and updates `aria-label`.
- Edit → View exits fullscreen first, since View mode has no toolbar/event handlers.
- Saved HTML is never modified by this viewport-only presentation.

This is **editor-only fullscreen** implemented with CSS `position: fixed`;
it does not use the browser's Fullscreen API or F11. The page surrounding
the editor is covered until fullscreen is exited.

## Compatibility

- MVC, Razor Pages and Blazor share `EditorMarkup` and the JavaScript engine.
- Existing Classic button, menu commands, custom commands, Export and
  Edit/View APIs remain unchanged.
- The `az-` namespace and dark-mode rules are retained. No dependency added.

## Tests

```powershell
python tests/editor-fullscreen-080-browser.py
python tests/editor-toolbar-split-080-browser.py
python tests/editor-066-view-export-browser.py
```

Also run `dotnet build -c Release` and exercise the actual MVC, Razor Pages
and Blazor Server host on the target system. This environment cannot verify
.NET 10 compilation or host behavior without the SDK.
