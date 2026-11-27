namespace Azunt.Editor.Models;

/// <summary>Controls the document's on-screen CSS without changing its HTML.</summary>
public enum EditorContentRendering
{
    /// <summary>Existing Azunt.Editor content appearance (default).</summary>
    Standard = 0,

    /// <summary>Browser-like typography and natural table layout for sanitized HTML.</summary>
    BrowserDefault = 1
}
