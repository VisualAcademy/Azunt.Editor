using Azunt.Editor.Storage;
using Azunt.Editor.Export.Models;

namespace Azunt.Web.Images;

// Example adapter. Production hosts must enforce document ownership and image ACLs.
public sealed class DemoImageResolver(FileSystemEditorImageStore store) : IEditorImageResolver
{
    public async ValueTask<EditorResolvedImage?> ResolveAsync(string imageUrl,
        CancellationToken cancellationToken = default)
    {
        var image = await store.ReadAsync(imageUrl, cancellationToken);
        return image is null ? null : new EditorResolvedImage(image.Content, image.ContentType);
    }
}
