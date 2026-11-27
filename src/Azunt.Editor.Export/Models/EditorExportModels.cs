namespace Azunt.Editor.Export.Models;

public enum EditorExportFormat
{
    Pdf,
    Docx,
    Xlsx
}

/// <summary>Formatting and output-file preferences for an export request.</summary>
public sealed class EditorExportOptions
{
    public string FileName { get; set; } = "Azunt-Document";
    public string Title { get; set; } = "Document";
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public string Margin { get; set; } = "normal";
    /// <summary>When true, PDF exports include current/total page counts in the footer.</summary>
    public bool ShowPageNumbers { get; set; } = false;
    /// <summary>Opt-in exact header names for native Excel date cells. Only the first all-th row defines columns.</summary>
    public IReadOnlyList<string> SpreadsheetDateColumns { get; set; } = Array.Empty<string>();
    /// <summary>Opt-in exact header names for native Excel numeric cells. Other values remain literal text.</summary>
    public IReadOnlyList<string> SpreadsheetNumberColumns { get; set; } = Array.Empty<string>();
    /// <summary>ISO date formats are supported by default; en-US additionally accepts month/day/year and AM/PM.</summary>
    public string SpreadsheetCulture { get; set; } = "";
}

public sealed record EditorExportResult(string FileName, string ContentType, byte[] Content);

public interface IEditorExportService
{
    Task<EditorExportResult> ExportAsync(
        string html, EditorExportFormat format, EditorExportOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>PDF configuration for a host with an installed Playwright Chromium browser.</summary>
public sealed class EditorPdfOptions
{
    /// <summary>Set when the host manages its own Chromium executable.</summary>
    public string? ExecutablePath { get; set; }
    /// <summary>Optional installed browser channel, for example "chrome" or "msedge".
    /// If unset, Playwright uses its version-matched Chromium headless shell.</summary>
    public string? BrowserChannel { get; set; }
    /// <summary>At most this many PDF renders at a time per service instance.</summary>
    public int MaxConcurrentExports { get; set; } = 2;
    public int RenderTimeoutMilliseconds { get; set; } = 60_000;
}

/// <summary>
/// Optional image resolver for exported documents. Implementations must only read
/// authenticated, host-owned references from trusted storage. Never HTTP-fetch a URL.
/// </summary>
public interface IEditorImageResolver
{
    ValueTask<EditorResolvedImage?> ResolveAsync(string imageUrl, CancellationToken cancellationToken = default);
}

public sealed record EditorResolvedImage(byte[] Content, string ContentType);
