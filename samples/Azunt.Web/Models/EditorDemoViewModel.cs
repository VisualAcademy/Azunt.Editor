namespace Azunt.Web.Models;

public sealed class EditorDemoViewModel
{
    public string Html { get; set; } = "<h2>Welcome to Azunt.Editor</h2><p>Edit this document and insert some images.</p>";
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public string Margin { get; set; } = "normal";
    public string? Notice { get; set; }
}
