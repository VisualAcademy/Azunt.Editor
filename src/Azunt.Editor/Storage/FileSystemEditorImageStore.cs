using System.Security.Cryptography;
using Azunt.Editor.Abstractions;

namespace Azunt.Editor.Storage;

/// <summary>
/// Optional local-file implementation for single-server applications and development.
/// Store files outside wwwroot. The host must supply authenticated upload/read endpoints,
/// enforce per-user/document authorization, and manage the lifetime of unused images.
/// For multi-instance deployments, use an IEditorImageStore backed by shared storage instead.
/// </summary>
public sealed class FileSystemEditorImageStore : IEditorImageStore, IEditorImageReader
{
    public const int MaximumImageBytes = 2 * 1024 * 1024;
    private readonly string _directory;
    private readonly string _publicPrefix;

    public FileSystemEditorImageStore(string directory, string publicUrlPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicUrlPrefix);
        if (!publicUrlPrefix.StartsWith('/') || publicUrlPrefix.StartsWith("//") ||
            publicUrlPrefix.Contains('?') || publicUrlPrefix.Contains('#') ||
            publicUrlPrefix.Contains('\\') || publicUrlPrefix.Contains("..", StringComparison.Ordinal) ||
            publicUrlPrefix.Any(char.IsControl))
            throw new ArgumentException("Image URL prefix must be a safe root-relative path.", nameof(publicUrlPrefix));
        _directory = Path.GetFullPath(directory);
        _publicPrefix = publicUrlPrefix.TrimEnd('/');
    }

    /// <summary>Recognize only images issued by this store. No arbitrary file paths or URLs.</summary>
    public bool IsManagedUrl(string? url) => TryParseUrl(url, out _);

    public async Task<EditorImageResult> SaveAsync(Stream content, string fileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        // Read at most Max + 1 bytes even when the caller supplies a non-seekable stream.
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await content.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumImageBytes)
                throw new ArgumentException("Image exceeds the 2 MB limit.", nameof(content));
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        var extension = GetImageExtension(bytes, contentType);
        if (extension is null) throw new ArgumentException("Unsupported image content.", nameof(contentType));
        Directory.CreateDirectory(_directory);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var file = Path.Combine(_directory, id + extension);
            FileStream output;
            try
            {
                output = new FileStream(file, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 8192, FileOptions.Asynchronous);
            }
            catch (IOException) when (File.Exists(file))
            {
                // A name collision must not delete or overwrite the existing image.
                continue;
            }
            try
            {
                await using (output)
                    await output.WriteAsync(bytes, cancellationToken);
            }
            catch
            {
                // This stream was created by us. Remove partial writes only.
                if (File.Exists(file)) File.Delete(file);
                throw;
            }
            return new EditorImageResult($"{_publicPrefix}/{id}", Path.GetFileName(fileName));
        }
        throw new IOException("Could not allocate a unique image filename.");
    }

    public async Task<EditorStoredImage?> ReadAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (!TryParseUrl(imageUrl, out var id)) return null;
        foreach (var (extension, contentType) in KnownTypes)
        {
            var path = Path.Combine(_directory, id + extension);
            if (!File.Exists(path)) continue;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 8192, FileOptions.Asynchronous);
            if (stream.Length is <= 0 or > MaximumImageBytes) return null;
            var data = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(data, cancellationToken);
            if (GetImageExtension(data, contentType) != extension) return null;
            return new EditorStoredImage(data, contentType);
        }
        return null;
    }

    // Intentionally no public delete HTTP endpoint. Hosts must check that a stored
    // image is unreferenced before deleting it (undo, drafts, and other documents).
    public Task<bool> DeleteUnreferencedAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (!TryParseUrl(imageUrl, out var id)) return Task.FromResult(false);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var (extension, _) in KnownTypes)
        {
            var file = Path.Combine(_directory, id + extension);
            if (!File.Exists(file)) continue;
            File.Delete(file);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private bool TryParseUrl(string? url, out string id)
    {
        id = "";
        if (string.IsNullOrWhiteSpace(url) ||
            !url.StartsWith(_publicPrefix + "/", StringComparison.Ordinal)) return false;
        var candidate = url[(_publicPrefix.Length + 1)..];
        if (candidate.Length != 32 || !candidate.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return false;
        id = candidate;
        return true;
    }

    private static readonly (string Extension, string ContentType)[] KnownTypes =
    [
        (".png", "image/png"), (".jpg", "image/jpeg"), (".gif", "image/gif"), (".webp", "image/webp")
    ];

    private static string? GetImageExtension(byte[] bytes, string contentType)
    {
        if (bytes.Length < 12 || string.IsNullOrWhiteSpace(contentType)) return null;
        return contentType.ToLowerInvariant() switch
        {
            "image/png" when bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => ".png",
            "image/jpeg" when bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[^2] == 0xff && bytes[^1] == 0xd9 => ".jpg",
            "image/gif" when bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8) => ".gif",
            "image/webp" when bytes.AsSpan().StartsWith("RIFF"u8) && bytes.AsSpan(8).StartsWith("WEBP"u8) => ".webp",
            _ => null
        };
    }
}
