using System.Text.RegularExpressions;

namespace Azunt.Editor.Models;

/// <summary>A host-defined document preset. Values are restricted to safe typography and spacing settings.</summary>
public sealed record EditorDocumentStyle
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string FontFamily { get; init; } = "System";
    public int FontSize { get; init; } = 13;
    public int TableFontSize { get; init; } = 12;
    public double LineHeight { get; init; } = 1.4;
    public int Padding { get; init; } = 12;
    public int CellPaddingVertical { get; init; } = 3;
    public int CellPaddingHorizontal { get; init; } = 5;
    public string BorderColor { get; init; } = "#cbd2da";
    public string HeaderColor { get; init; } = "#f1f4f7";
    public string TextColor { get; init; } = "#202124";

    private static string? CssFont(string family) => family switch
    {
        "System" => "system-ui, -apple-system, \"Segoe UI\", Arial, sans-serif",
        "Arial" => "Arial, sans-serif", "Georgia" => "Georgia, serif",
        "TimesNewRoman" => "\"Times New Roman\", serif", "Consolas" => "Consolas, monospace", _ => null
    };

    internal static EditorDocumentStyle[] Validate(IReadOnlyList<EditorDocumentStyle>? styles)
    {
        if (styles is null) return [];
        if (styles.Count > 20) throw new ArgumentException("At most 20 custom document styles are supported.", nameof(styles));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        static bool Color(string? value) => value is not null && Regex.IsMatch(value, @"\A#[0-9a-fA-F]{6}\z");
        foreach (var style in styles)
        {
            if (style is null || style.Id is null || !Regex.IsMatch(style.Id, @"\A[a-z][a-z0-9-]{0,39}\z") || !ids.Add(style.Id) ||
                string.IsNullOrWhiteSpace(style.Name) || style.Name.Length > 80 || CssFont(style.FontFamily) is null ||
                style.FontSize is < 8 or > 32 || style.TableFontSize is < 8 or > 32 ||
                !double.IsFinite(style.LineHeight) || style.LineHeight is < 1 or > 2.5 || style.Padding is < 0 or > 60 ||
                style.CellPaddingVertical is < 0 or > 24 || style.CellPaddingHorizontal is < 0 or > 24 ||
                !Color(style.BorderColor) || !Color(style.HeaderColor) || !Color(style.TextColor))
                throw new ArgumentException("Invalid or duplicate custom document style.", nameof(styles));
        }
        return styles.ToArray();
    }

    // Called only after Validate. The server-rendered viewer and the live editor
    // use the same restricted settings, including before JS hydration.
    internal string CssVariables() => FormattableString.Invariant(
        $"--azunt-document-font:{CssFont(FontFamily)};--azunt-document-size:{FontSize}px;--azunt-document-line:{LineHeight};--azunt-document-padding:{Padding}px;--azunt-document-cell-size:{TableFontSize}px;--azunt-document-cell-padding:{CellPaddingVertical}px {CellPaddingHorizontal}px;--azunt-document-border:{BorderColor};--azunt-document-header:{HeaderColor};--azunt-document-color:{TextColor}");
}
