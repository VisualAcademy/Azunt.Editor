using Azunt.Editor.Export.Models;

namespace Azunt.Editor.Export.Word;

// Only consumed by the DOCX writer; other export formats never validate these settings.
internal sealed record WordLayoutSettings(int? BodySize, int? TableSize, int? CellPadding,
    bool CompactSpacing, int PageWidth, int PageHeight)
{
    public int BodyImageWidth => Math.Clamp((PageWidth - 30) / 15, 1, 560);
    public int ImageHeight => Math.Clamp((PageHeight - 360) / 15, 1, 1200);

    public static WordLayoutSettings Create(EditorExportOptions options, int pageWidth, int pageHeight)
    {
        var defaults = options.WordLayout switch
        {
            EditorWordLayout.Standard => (Body: (int?)null, Table: (int?)null, Padding: (int?)null),
            EditorWordLayout.Compact => (Body: (int?)10, Table: (int?)9, Padding: (int?)3),
            EditorWordLayout.Comfortable => (Body: (int?)14, Table: (int?)12, Padding: (int?)8),
            _ => throw new ArgumentOutOfRangeException(nameof(options.WordLayout))
        };
        Check(options.WordFontSize, 8, 36, nameof(options.WordFontSize));
        Check(options.WordTableFontSize, 8, 36, nameof(options.WordTableFontSize));
        Check(options.WordTableCellPadding, 0, 24, nameof(options.WordTableCellPadding));
        return new WordLayoutSettings(options.WordFontSize ?? defaults.Body,
            options.WordTableFontSize ?? defaults.Table, options.WordTableCellPadding ?? defaults.Padding,
            options.WordLayout == EditorWordLayout.Compact, pageWidth, pageHeight);
    }

    private static void Check(int? value, int min, int max, string name)
    {
        if (value.HasValue && (value.Value < min || value.Value > max))
            throw new ArgumentOutOfRangeException(name, $"Value must be between {min} and {max}.");
    }
}
