# Azunt.Editor
A lightweight rich-text editor for ASP.NET Core MVC, Razor Pages, and Blazor Server.

Version: **0.5.5** · .NET 10

## Features
- Image drag-and-drop
- Alternative text for images
- Refined image insertion and touch resizing
- Optional host-managed image uploads and local-file demo storage

## Projects
- `src/Azunt.Editor` — editor components
- `src/Azunt.Editor.Export` — PDF, Word and Excel export
- `samples/Azunt.Web` — example application
- `tests` — smoke and browser tests

## Run
```powershell
dotnet restore Azunt.Editor.sln
dotnet build Azunt.Editor.sln -c Release
dotnet run --project samples/Azunt.Web
```

## Image storage
The default editor keeps images inline (Base64). This behavior has not changed.

For a local development demo, run Azunt.Web and visit
`/editor/blazor?images=stored`, `/editor/mvc?images=stored`, or
`/editor/razor-pages?images=stored`. Uploaded files are saved under
`Azunt.Web/App_Data/editor-images`, outside `wwwroot`.

The sample upload and image routes are **Development-only and loopback-only**.
Production hosts should implement `IEditorImageStore` and `IEditorImageReader`
with authenticated endpoints, authorization, quotas, and shared storage when
running across multiple servers. `IEditorImageResolver` allows the export module
to embed authorized stored images without downloading untrusted URLs.

Do not delete images immediately when a user removes them in the editor.
Documents, drafts, and undo history may still reference them. Clean up only
once the host has verified that an image is unused.

## License
MIT. See [LICENSE](LICENSE).
