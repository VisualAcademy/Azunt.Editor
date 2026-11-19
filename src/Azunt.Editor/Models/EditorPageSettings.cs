namespace Azunt.Editor.Models;

/// <summary>Browser print settings exposed by Azunt.Editor without binding to an export package.</summary>
public sealed class EditorPageSettings
{
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public string Margin { get; set; } = "normal";
}
