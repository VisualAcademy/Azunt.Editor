namespace Azunt.Editor.Abstractions;

/// <summary>
/// Host-provided image storage. The editor does not choose a database, tenant, or storage provider.
/// Implementations must validate file content and enforce storage limits.
/// </summary>
public interface IEditorImageStore
{
    Task<EditorImageResult> SaveAsync(Stream content, string fileName, string contentType,
        CancellationToken cancellationToken = default);
}

public sealed record EditorImageResult(string Url, string? AltText = null);

/// <summary>
/// Optional host-controlled lookup of editor images. Do not resolve untrusted URLs by HTTP.
/// Access checks and document ownership remain the responsibility of the host.
/// </summary>
public interface IEditorImageReader
{
    Task<EditorStoredImage?> ReadAsync(string imageUrl, CancellationToken cancellationToken = default);
}

public sealed record EditorStoredImage(byte[] Content, string ContentType);
