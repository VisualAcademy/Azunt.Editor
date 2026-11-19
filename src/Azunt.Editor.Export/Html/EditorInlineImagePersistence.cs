using System.Security.Cryptography;

namespace Azunt.Editor.Export.Html;

/// <summary>
/// Replaces validated inline raster images with host-managed image references.
/// The host owns image storage, authorization, document persistence, and cleanup.
/// This service never reads remote URLs or deletes stored images.
/// </summary>
public sealed class EditorInlineImagePersistence
{
    public const int MaximumImages = 32;
    public const int MaximumImageBytes = 2 * 1024 * 1024;
    public const int MaximumTotalImageBytes = 12 * 1024 * 1024;

    /// <param name="saveImageAsync">Store image bytes and return a host-owned root-relative image URL.
    /// Parameters are content stream, file name, content type, and cancellation token.</param>
    /// <param name="isManagedUrl">Validate that a returned or existing URL belongs to this host and is authorized for this document.</param>
    public async Task<EditorImagePersistenceResult> PersistAsync(
        string html,
        Func<Stream, string, string, CancellationToken, Task<string>> saveImageAsync,
        Func<string, bool> isManagedUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saveImageAsync);
        ArgumentNullException.ThrowIfNull(isManagedUrl);
        cancellationToken.ThrowIfCancellationRequested();

        var document = SafeEditorDocument.Parse(html);
        var nodes = document.Root.DescendantsAndSelf().Where(node => node.Name == "img").ToList();
        if (nodes.Count > MaximumImages)
            throw new ArgumentException($"Document exceeds the {MaximumImages}-image limit.", nameof(html));

        // Validate every image BEFORE performing the first write, so bad input does
        // not create a partial set of uploaded files.
        var pending = new List<(SafeEditorDocument.Node Node, byte[] Bytes, string ContentType, string Hash)>();
        long totalBytes = 0;
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var src = node.Attribute("src");
            if (SafeEditorDocument.TryInlineImage(node, out var contentType, out var bytes))
            {
                if (bytes.Length > MaximumImageBytes)
                    throw new ArgumentException("Inline image exceeds the 2 MB storage limit.", nameof(html));
                totalBytes += bytes.Length;
                if (totalBytes > MaximumTotalImageBytes)
                    throw new ArgumentException("Document images exceed the 12 MB total limit.", nameof(html));
                pending.Add((node, bytes, contentType, Convert.ToHexString(SHA256.HashData(bytes))));
            }
            else if (!SafeEditorDocument.IsSafeLocalImageReference(src) || !isManagedUrl(src))
            {
                // Do not silently remove or replace an unrecognized image.
                throw new ArgumentException("Document contains an unsupported image reference.", nameof(html));
            }
        }

        var urlsByContent = new Dictionary<string, string>(StringComparer.Ordinal);
        var savedImages = 0;
        foreach (var (node, bytes, contentType, hash) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = contentType + ":" + hash;
            if (!urlsByContent.TryGetValue(key, out var imageUrl))
            {
                var extension = contentType switch
                {
                    "image/png" => ".png", "image/jpeg" => ".jpg",
                    "image/gif" => ".gif", "image/webp" => ".webp",
                    _ => throw new ArgumentException("Unsupported image type.", nameof(html))
                };
                using var stream = new MemoryStream(bytes, writable: false);
                imageUrl = await saveImageAsync(stream, "image" + extension, contentType, cancellationToken);
                if (!SafeEditorDocument.IsSafeLocalImageReference(imageUrl) || !isManagedUrl(imageUrl))
                    throw new InvalidOperationException("Image store returned an unrecognized image URL.");
                urlsByContent.Add(key, imageUrl);
                savedImages++;
            }
            node.Attributes["src"] = imageUrl;
        }

        // Uses the same restricted HTML serializer as the existing export pipeline.
        return new EditorImagePersistenceResult(document.ToSafeHtml(isManagedUrl),
            nodes.Count, savedImages);
    }
}

public sealed record EditorImagePersistenceResult(string Html, int ImageCount, int SavedImageCount);
