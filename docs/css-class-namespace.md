# Azunt.Editor CSS class namespace — 0.6.3

All editor-owned CSS classes now use the `az-` prefix in markup and styles.
This includes the editor itself, Compact/Classic toolbars, editor panels,
context tools, selected images, image resize handles, preview pages, and the
optional export button group. This is a deliberate breaking change for
**custom host selectors**; the editing APIs and persisted HTML remain unchanged.

| Prior selector | Replacement |
| --- | --- |
| `.azunt-editor` | `.az-azunt-editor` |
| `.azunt-editor-toolbar` | `.az-azunt-editor-toolbar` |
| `.azunt-editor-button` | `.az-azunt-editor-button` |
| `.azunt-editor-canvas` | `.az-azunt-editor-canvas` |
| `.azunt-editor-image-selected` | `.az-azunt-editor-image-selected` |
| `.azunt-editor-menu-panel` | `.az-azunt-editor-menu-panel` |
| `.azunt-editor-export-actions` | `.az-azunt-editor-export-actions` |
| `.azunt-print-page` | `.az-azunt-print-page` |
| `.is-resizing` (editor image handle) | `.az-is-resizing` |

Update any host CSS rules and custom JS DOM selectors that reference the old
classes. `data-*` attributes are unchanged, so using stable data attributes
is recommended for automation and extensions where available.

Both `Azunt.Editor` and `Azunt.Editor.Export` packages must be upgraded to
0.6.3 together for consistent markup and styles; clear cached static CSS/JS
resources on deployment. Do not rename asset paths such as
`/_content/Azunt.Editor/css/azunt-editor.css` or Razor Tag Helpers such as
`<azunt-editor>`.

This namespace is for product identification; the MIT license is unaffected.

## Complete class rename reference (53 entries)

| 0.6.2 class | 0.6.3 class |
| --- | --- |
| `.azunt-editor` | `.az-azunt-editor` |
| `.azunt-editor-alt` | `.az-azunt-editor-alt` |
| `.azunt-editor-button` | `.az-azunt-editor-button` |
| `.azunt-editor-button-icon` | `.az-azunt-editor-button-icon` |
| `.azunt-editor-button-label` | `.az-azunt-editor-button-label` |
| `.azunt-editor-canvas` | `.az-azunt-editor-canvas` |
| `.azunt-editor-chars-panel` | `.az-azunt-editor-chars-panel` |
| `.azunt-editor-classic-groups` | `.az-azunt-editor-classic-groups` |
| `.azunt-editor-context-tools` | `.az-azunt-editor-context-tools` |
| `.azunt-editor-current-format` | `.az-azunt-editor-current-format` |
| `.azunt-editor-custom-button` | `.az-azunt-editor-custom-button` |
| `.azunt-editor-custom-group` | `.az-azunt-editor-custom-group` |
| `.azunt-editor-date-panel` | `.az-azunt-editor-date-panel` |
| `.azunt-editor-export-actions` | `.az-azunt-editor-export-actions` |
| `.azunt-editor-find-panel` | `.az-azunt-editor-find-panel` |
| `.azunt-editor-find-status` | `.az-azunt-editor-find-status` |
| `.azunt-editor-footer` | `.az-azunt-editor-footer` |
| `.azunt-editor-fullscreen` | `.az-azunt-editor-fullscreen` |
| `.azunt-editor-group` | `.az-azunt-editor-group` |
| `.azunt-editor-image-drop-active` | `.az-azunt-editor-image-drop-active` |
| `.azunt-editor-image-resize-handle` | `.az-azunt-editor-image-resize-handle` |
| `.azunt-editor-image-selected` | `.az-azunt-editor-image-selected` |
| `.azunt-editor-input-label` | `.az-azunt-editor-input-label` |
| `.azunt-editor-link-panel` | `.az-azunt-editor-link-panel` |
| `.azunt-editor-menu-empty` | `.az-azunt-editor-menu-empty` |
| `.azunt-editor-menu-panel` | `.az-azunt-editor-menu-panel` |
| `.azunt-editor-menu-row` | `.az-azunt-editor-menu-row` |
| `.azunt-editor-menu-trigger` | `.az-azunt-editor-menu-trigger` |
| `.azunt-editor-message` | `.az-azunt-editor-message` |
| `.azunt-editor-narrow` | `.az-azunt-editor-narrow` |
| `.azunt-editor-panel-heading` | `.az-azunt-editor-panel-heading` |
| `.azunt-editor-quick-command` | `.az-azunt-editor-quick-command` |
| `.azunt-editor-quick-controls` | `.az-azunt-editor-quick-controls` |
| `.azunt-editor-quick-custom` | `.az-azunt-editor-quick-custom` |
| `.azunt-editor-quick-group` | `.az-azunt-editor-quick-group` |
| `.azunt-editor-quick-group-optional` | `.az-azunt-editor-quick-group-optional` |
| `.azunt-editor-quick-label` | `.az-azunt-editor-quick-label` |
| `.azunt-editor-quick-more` | `.az-azunt-editor-quick-more` |
| `.azunt-editor-quick-optional` | `.az-azunt-editor-quick-optional` |
| `.azunt-editor-quick-row` | `.az-azunt-editor-quick-row` |
| `.azunt-editor-select` | `.az-azunt-editor-select` |
| `.azunt-editor-signature-note` | `.az-azunt-editor-signature-note` |
| `.azunt-editor-signature-panel` | `.az-azunt-editor-signature-panel` |
| `.azunt-editor-source` | `.az-azunt-editor-source` |
| `.azunt-editor-stats` | `.az-azunt-editor-stats` |
| `.azunt-editor-template-panel` | `.az-azunt-editor-template-panel` |
| `.azunt-editor-tiny` | `.az-azunt-editor-tiny` |
| `.azunt-editor-toolbar` | `.az-azunt-editor-toolbar` |
| `.azunt-editor-value` | `.az-azunt-editor-value` |
| `.azunt-editor-very-narrow` | `.az-azunt-editor-very-narrow` |
| `.azunt-editor-width` | `.az-azunt-editor-width` |
| `.azunt-print-page` | `.az-azunt-print-page` |
| `.is-resizing` | `.az-is-resizing` |
