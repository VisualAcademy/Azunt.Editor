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
        if (options.DocumentStyle is { } style)
        {
            style.Validate();
            // Opt-in document rendering overrides the legacy density profile, not inline HTML.
            var line = style.BrowserDefaults ? "normal" : style.LineHeight.ToString(CultureInfo.InvariantCulture);
            var width = style.FullWidthTables ? "100%" : "auto";
            var border = style.BrowserDefaults ? "none" : "1px solid " + style.BorderColor;
            var header = style.BrowserDefaults ? "transparent" : style.HeaderColor;
            var collapse = style.BrowserDefaults ? "separate" : "collapse";
            var margin = style.BrowserDefaults ? "0" : style.FullWidthTables ? "12px 0" : "8px 0";
            var wrap = style.FullWidthTables ? "anywhere" : "normal";
            css = FormattableString.Invariant($$"""
                body{font-family:{{style.CssFont}};font-size:{{style.FontSize}}px;line-height:{{line}};color:{{style.TextColor}}}
                p{margin:{{style.ParagraphMarginEm}}em 0}body>:first-child{margin-top:0}
                p,li,td,th{overflow-wrap:{{wrap}}}
                table{width:{{width}};max-width:100%;font-size:{{style.TableFontSize}}px;margin:{{margin}};border-collapse:{{collapse}}}
                table[style*="table-layout:fixed"],table[style*="table-layout: fixed"]{width:100%}
                td,th{font:inherit;color:inherit;padding:{{style.CellPaddingVertical}}px {{style.CellPaddingHorizontal}}px;border:{{border}};vertical-align:top}
                th{font-weight:600;background:{{header}}}
                """);
            css += style.BrowserDefaults
                ? "\ntable{border-spacing:2px}td,th{vertical-align:middle}th{font-weight:bold;text-align:center}body>:first-child{margin-top:revert}blockquote{border:0;padding:0;margin:1em 40px}a{color:revert}"
                : "\nh2{font-size:1.5em;line-height:1.25;margin:.75em 0 .5em}th{text-align:center}img{vertical-align:middle}";
        }
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
