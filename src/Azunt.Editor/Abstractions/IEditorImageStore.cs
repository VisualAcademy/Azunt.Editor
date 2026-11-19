namespace Azunt.Editor.Abstractions;

/// <summary>
/// Optional host-implemented image storage; do not implement business-specific storage in the editor.
/// </summary>
public interface IEditorImageStore
{
    Task<EditorImageResult> SaveAsync(Stream content, string fileName, string contentType,
        CancellationToken cancellationToken = default);
}

public sealed record EditorImageResult(string Url, string? AltText = null);
