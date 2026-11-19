using Azunt.Editor.Export.Html;
using Azunt.Editor.Storage;

namespace Azunt.Web.Images;

// Demo adapter for final-save image conversion. Only call from Development-only paths.
internal static class DemoImageSaveService
{
    public static Task<EditorImagePersistenceResult> PersistAsync(
        EditorInlineImagePersistence persistence, FileSystemEditorImageStore store,
        string html, CancellationToken cancellationToken = default)
        => persistence.PersistAsync(html, async (content, fileName, contentType, token) =>
        {
            var saved = await store.SaveAsync(content, fileName, contentType, token);
            return saved.Url;
        }, store.IsManagedUrl, cancellationToken);
}
