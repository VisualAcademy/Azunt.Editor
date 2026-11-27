using System.Text.RegularExpressions;

namespace Azunt.Editor.Export.Models;

/// <summary>Opt-in document typography for PDF and Word. Pixel sizes use CSS pixels (96 dpi).
/// Independent of the editor package; a host can map its registered custom preset to this record.</summary>
public sealed record EditorExportDocumentStyle
{
    public string FontFamily { get; init; } = "System";
    public int FontSize { get; init; } = 13;
    public int TableFontSize { get; init; } = 12;
    public double LineHeight { get; init; } = 1.4;
    public int CellPaddingVertical { get; init; } = 3;
    public int CellPaddingHorizontal { get; init; } = 5;
    public string BorderColor { get; init; } = "#cbd2da";
    public string HeaderColor { get; init; } = "#f1f4f7";
    public string TextColor { get; init; } = "#202124";
    public bool BrowserDefaults { get; init; }
    public bool FullWidthTables { get; init; }
    public double ParagraphMarginEm { get; init; } = .45;

    /// <summary>Resolve a built-in editor style ID. Custom IDs must be mapped by the host.</summary>
    public static EditorExportDocumentStyle FromPreset(string id) => id switch
    {
        "Standard" => new() { FontSize = 15, TableFontSize = 15, LineHeight = 1.6,
            CellPaddingVertical = 8, CellPaddingHorizontal = 8, BorderColor = "#aeb8c6",
            HeaderColor = "#f1f5f9", TextColor = "#0f172a", FullWidthTables = true, ParagraphMarginEm = .6 },
        "BrowserDefault" => new() { FontFamily = "TimesNewRoman", FontSize = 16, TableFontSize = 16,
            LineHeight = 1.2, CellPaddingVertical = 1, CellPaddingHorizontal = 1,
            TextColor = "#000000", BrowserDefaults = true, ParagraphMarginEm = 1 },
        "CompactReport" => new(),
        "BusinessDocument" => new() { FontSize = 14, TableFontSize = 13, LineHeight = 1.5,
            CellPaddingVertical = 6, CellPaddingHorizontal = 8, BorderColor = "#b9c8d8", HeaderColor = "#e8eff6" },
        "Minimal" => new() { FontSize = 14, TableFontSize = 13, LineHeight = 1.45,
            CellPaddingVertical = 4, CellPaddingHorizontal = 6, BorderColor = "#e1e4e8", HeaderColor = "#fafafa" },
        _ => throw new ArgumentException("Unknown built-in document style; map custom styles explicitly.", nameof(id))
    };

    internal string CssFont => FontFamily switch
    {
        "System" => "system-ui, -apple-system, 'Segoe UI', Arial, sans-serif",
        "Arial" => "Arial, sans-serif", "Georgia" => "Georgia, serif",
        "TimesNewRoman" => "'Times New Roman', serif", "Consolas" => "Consolas, monospace",
        _ => throw new ArgumentException("Unsupported document font family.", nameof(FontFamily))
    };
    internal string WordFont => FontFamily switch
    {
        "System" => "Segoe UI", "TimesNewRoman" => "Times New Roman", _ => FontFamily
    };

    internal void Validate()
    {
        _ = CssFont;
        static bool Color(string? value) => value is not null && Regex.IsMatch(value, @"\A#[0-9a-fA-F]{6}\z");
        if (FontSize is < 8 or > 32 || TableFontSize is < 8 or > 32 ||
            !double.IsFinite(LineHeight) || LineHeight is < 1 or > 2.5 ||
            CellPaddingVertical is < 0 or > 24 || CellPaddingHorizontal is < 0 or > 24 ||
            !double.IsFinite(ParagraphMarginEm) || ParagraphMarginEm is < 0 or > 2 ||
            !Color(BorderColor) || !Color(HeaderColor) || !Color(TextColor))
            throw new ArgumentException("Invalid export document style.");
    }
}
