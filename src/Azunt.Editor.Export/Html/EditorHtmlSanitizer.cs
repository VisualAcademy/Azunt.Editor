namespace Azunt.Editor.Export.Html;

/// <summary>
/// Produces a restricted HTML subset suitable for Azunt.Editor document storage
/// and rendering. This is NOT a general-purpose sanitizer for arbitrary HTML,
/// and hosts must still apply authentication, authorization, CSP and upload limits.
/// </summary>
public static class EditorHtmlSanitizer
{
    public const int MaxHtmlCharacters = 12_000_000;

    /// <summary>
    /// Strips unsupported elements, attributes and styles; only inline validated
    /// PNG/JPEG/GIF/WebP data images are kept. External images are intentionally
    /// removed to avoid unintended server-side requests and privacy leakage.
    /// </summary>
    public static string Sanitize(string html)
        => SafeEditorDocument.Parse(html).ToSafeHtml();
}
