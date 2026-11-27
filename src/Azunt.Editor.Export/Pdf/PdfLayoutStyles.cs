using Azunt.Editor.Export.Models;
using System.Globalization;

namespace Azunt.Editor.Export.Pdf;

/// <summary>Only trusted CSS constants and validated numeric values enter the print stylesheet.</summary>
internal static class PdfLayoutStyles
{
    internal const string Compact = """
        body{font-size:10pt;line-height:1.2}
        table{font-size:9pt;margin:6px 0}
        td,th{padding:3px;border-width:0.5px;border-color:#b0b8c4}
        td>p,th>p{margin:0 0 2px}td>p:last-child,th>p:last-child{margin-bottom:0}
        """;

    internal const string Comfortable = """
        body{font-size:14pt;line-height:1.6}
        table{font-size:12pt;margin:12px 0}
        td,th{padding:8px}
        """;

    public static string Build(EditorExportOptions options)
    {
        var css = options.PdfLayout switch
        {
            EditorPdfLayout.Standard => string.Empty,
            EditorPdfLayout.Compact => Compact,
            EditorPdfLayout.Comfortable => Comfortable,
            _ => throw new ArgumentOutOfRangeException(nameof(options.PdfLayout))
        };
        // Avoid !important: explicitly supported inline styles remain authoritative.
        if (options.PdfFontSize is int body)
            css += FontRule("body", body, nameof(options.PdfFontSize));
        if (options.PdfTableFontSize is int table)
            css += FontRule("table", table, nameof(options.PdfTableFontSize));
        if (options.PdfTableCellPadding is int padding)
        {
            if (padding is < 0 or > 24)
                throw new ArgumentOutOfRangeException(nameof(options.PdfTableCellPadding), "PDF cell padding must be between 0 and 24 pixels.");
            css += "\ntd,th{padding:" + padding.ToString(CultureInfo.InvariantCulture) + "px}";
        }
        return css;
    }

    public static string Wrap(string css) => css.Length == 0 ? string.Empty : "<style>" + css + "</style>";

    private static string FontRule(string selector, int size, string parameter)
    {
        if (size is < 8 or > 36)
            throw new ArgumentOutOfRangeException(parameter, "PDF font size must be between 8 and 36 points.");
        return "\n" + selector + "{font-size:" + size.ToString(CultureInfo.InvariantCulture) + "pt}";
    }
}
