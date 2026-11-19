# Image persistence

Azunt.Editor supports three image workflows:

| Mode | Insertion | Save HTML |
| --- | --- | --- |
| Inline (default) | Base64 in HTML | Keeps Base64 |
| Upload on insert | Stored file URL | Keeps file URL |
| Store on Save | Base64 in HTML | Converts inline images to file URLs |

The local demo modes are available only in Development. Visit `/editor/blazor?images=save`,
`/editor/mvc?images=save`, or `/editor/razor-pages?images=save` for save-time conversion.
The HTML source becomes shorter after saving. Local image binaries are stored outside
`wwwroot`, under `samples/Azunt.Web/App_Data/editor-images`.

## Host integration

The conversion service is provided by `Azunt.Editor.Export` and has no dependency
on a particular storage system. Register your own `IEditorImageStore` implementation
for filesystem or Azure Blob storage, then call:

```csharp
var result = await persistence.PersistAsync(
    html,
    async (content, name, mime, ct) =>
        (await imageStore.SaveAsync(content, name, mime, ct)).Url,
    imageStore.IsManagedUrl,
    cancellationToken);

// Persist result.Html to the application's document database.
```

`IsManagedUrl` in this example represents a host-provided URL authorization check.
It must validate ownership and document access, not merely a URL prefix. The demo
`FileSystemEditorImageStore.IsManagedUrl` checks format and prefix only, and is not
an authorization system.

`PersistAsync` validates all images before uploading. It supports PNG, JPEG, GIF,
and WebP, up to 2 MB each, 12 MB total, and 32 images per document. Duplicate
inline image content is uploaded once per Save call. Existing managed URLs are
retained and are not uploaded again. Unsupported or remote image URLs fail Save.

The store must return safe root-relative URLs. Do not return raw private Blob URLs
or URLs containing access tokens. Instead, return a short application URL that
checks permissions and streams the Blob to an authorized user. For exports, the
host should implement `IEditorImageResolver` to retrieve stored images without HTTP.

The image storage write and document database save are not one transaction. If a
later operation fails, an uploaded image may remain unused. Use a host-owned orphan
cleanup process after checking document references; do not delete an image merely
because it was removed from the editor. The sample does not persist document HTML
across application restarts.

## Image loading

Stored images use short URLs rather than Base64 HTML. This keeps the document
smaller and lets the browser reuse its cached image response. The local demo
returns `Cache-Control: private, max-age=86400, immutable` and an ETag because
new image content always receives a new random ID. These cache settings apply
only to the local Development example. Production hosts must check permissions
before reading private images and choose cache settings appropriate to their
authorization model. Never use shared public caches for protected documents.
