namespace Azunt.Editor.Models;

/// <summary>Controls the document's on-screen CSS without changing its HTML.</summary>
public enum EditorContentRendering
{
    /// <summary>Existing Azunt.Editor content appearance (default).</summary>
    Standard = 0,

    /// <summary>Browser-like typography and natural table layout for sanitized HTML.</summary>
    BrowserDefault = 1,

    /// <summary>Small, readable sans-serif typography and dense report tables.</summary>
    CompactReport = 2,

    /// <summary>Clean business document with lightly emphasized table headings.</summary>
    BusinessDocument = 3,

    /// <summary>Restrained typography and subtle table borders.</summary>
    Minimal = 4
}
