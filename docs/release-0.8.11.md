# 0.8.11 — Custom document styles

- Add per-instance, validated custom document styles for Blazor, MVC/Razor Pages and JavaScript.
- Preserve all built-in styles and existing rendering APIs.
- Apply custom styles to browser Preview and Print (Match document).
- Add a custom style example and an independent Word output layout selector (Compact by default) to the Blazor demo.
- No new package dependencies or server export engine changes.

Style IDs are host-managed metadata, not saved HTML. Server export style mapping is a separate task.
