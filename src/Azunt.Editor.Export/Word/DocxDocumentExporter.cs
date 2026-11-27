using Azunt.Editor.Export.Html;
using Azunt.Editor.Export.Formatting;
using Azunt.Editor.Export.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Azunt.Editor.Export.Word;

internal static class DocxDocumentExporter
{
    private readonly record struct Format(bool Bold = false, bool Italic = false, bool Underline = false,
        bool Strike = false, string? Script = null, string? Font = null, double? Size = null,
        string? Color = null, string? Highlight = null);

    public static byte[] Export(SafeEditorDocument document, EditorExportOptions settings)
    {
        using var buffer = new MemoryStream();
        using (var package = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart();
            var body = new W.Body();
            main.Document = new W.Document(body);
            var landscape = settings.Orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase);
            var size = settings.PaperSize switch
            {
                "Letter" => (Width: 12240U, Height: 15840U),
                "Legal" => (Width: 12240U, Height: 20160U),
                _ => (Width: 11906U, Height: 16838U)
            };
            var margin = settings.Margin switch { "narrow" => 454, "wide" => 1417, _ => 907 };
            var pageWidth = (int)(landscape ? size.Height : size.Width) - margin * 2;
            var pageHeight = (int)(landscape ? size.Width : size.Height) - margin * 2;
            var layout = WordLayoutSettings.Create(settings, pageWidth, pageHeight);
            if (layout.BodySize is double bodySize)
            {
                var styles = main.AddNewPart<StyleDefinitionsPart>();
                var runDefaults = new W.RunPropertiesBaseStyle(
                    new W.FontSize { Val = WordLayoutSettings.HalfPoints(bodySize) },
                    new W.FontSizeComplexScript { Val = WordLayoutSettings.HalfPoints(bodySize) });
                var docDefaults = new W.DocDefaults(new W.RunPropertiesDefault(runDefaults));
                if (layout.DocumentStyle is { } style)
                {
                    runDefaults.AddChild(new W.RunFonts { Ascii = style.WordFont, HighAnsi = style.WordFont, EastAsia = style.WordFont });
                    runDefaults.AddChild(new W.Color { Val = style.TextColor[1..] });
                    // Body spacing approximates collapsed HTML paragraph margins in Word.
                    docDefaults.Append(new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                        new W.SpacingBetweenLines { Before = "0", After = ((int)Math.Round(bodySize * 20 * style.ParagraphMarginEm)).ToString(CultureInfo.InvariantCulture),
                            Line = ((int)Math.Round(style.LineHeight * 240)).ToString(CultureInfo.InvariantCulture), LineRule = W.LineSpacingRuleValues.Auto })));
                }
                styles.Styles = new W.Styles(docDefaults);
                styles.Styles.Save();
            }
            uint drawingId = 1;
            foreach (var child in document.Root.Children) AppendBlock(child, body, main, layout, ref drawingId);
            if (!body.Elements<W.Paragraph>().Any() && !body.Elements<W.Table>().Any()) body.Append(new W.Paragraph());
            body.Append(new W.SectionProperties(
                new W.PageSize { Width = landscape ? size.Height : size.Width,
                                 Height = landscape ? size.Width : size.Height,
                                 Orient = landscape ? W.PageOrientationValues.Landscape : W.PageOrientationValues.Portrait },
                new W.PageMargin { Top = margin, Bottom = margin, Left = (uint)margin, Right = (uint)margin,
                                   Header = 360U, Footer = 360U, Gutter = 0U }));
            main.Document.Save();
        }
        return buffer.ToArray();
    }

    private static void AppendBlock(SafeEditorDocument.Node node, W.Body body, MainDocumentPart part, WordLayoutSettings layout, ref uint drawingId)
    {
        if (node.Name == "hr")
        {
            // A semantic editor page break becomes a real Word page break.
            // Ordinary horizontal rules remain visible text separators.
            var paragraph = new W.Paragraph();
            if (node.Attribute("data-azunt-page-break") == "true")
                paragraph.Append(new W.Run(new W.Break { Type = W.BreakValues.Page }));
            else
                paragraph.Append(new W.Run(new W.Text("────────────────────")));
            body.Append(paragraph);
            return;
        }
        if (node.Name == "table")
        {
            body.Append(BuildTable(node, part, layout, ref drawingId));
            return;
        }
        if (node.Name is "ul" or "ol")
        {
            var i = node.Name == "ol" && int.TryParse(node.Attribute("start"), out var start) && start is >= 1 and <= 9999 ? start - 1 : 0;
            foreach (var item in node.Children.Where(c => c.Name == "li"))
            {
                i++;
                var para = Paragraph(item);
                para.Append(new W.Run(new W.Text(node.Name == "ul" ? "• " : LegacyListMarker(i, node.Style("list-style-type")) + ". ")));
                AppendInline(item, para, part, new Format(), ref drawingId, layout.BodyImageWidth, layout.ImageHeight);
                body.Append(para);
            }
            return;
        }
        if (node.Name is "p" or "h1" or "h2" or "h3" or "h4" or "li" or "blockquote" or "pre")
        {
            var para = Paragraph(node);
            var format = node.Name switch
            {
                "h1" => new Format(Bold: true, Size: 28),
                "h2" => new Format(Bold: true, Size: 22),
                "h3" => new Format(Bold: true, Size: 18),
                "h4" => new Format(Bold: true, Size: 14),
                _ => new Format()
            };
            if (layout.DocumentStyle is not null && layout.BodySize is double baseSize && node.Name is ("h1" or "h2" or "h3" or "h4"))
                format = format with { Size = baseSize * (node.Name switch { "h1" => 2, "h2" => 1.5, "h3" => 1.17, _ => 1 }) };
            if (node.Name == "pre")
            {
                // Code is stored as plain text; do not generate syntax-markup in the file.
                // Word requires explicit break/tab elements: newlines inside w:t are not rendered.
                var properties = para.GetFirstChild<W.ParagraphProperties>();
                properties?.AddChild(new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = "F1F5F9" });
                var code = node.Children.FirstOrDefault(child => child.Name == "code");
                var codeFormat = new Format(Font: "Consolas", Size: 10, Color: "0F172A");
                var source = code?.PlainText() ?? node.PlainText();
                var language = node.Attribute("data-language");
                foreach (var token in CodeSyntaxTokens.Tokenize(source, language))
                {
                    var color = CodeSyntaxTokens.Color(token.Category);
                    AppendPreformattedText(token.Text, para,
                        color is null ? codeFormat : codeFormat with { Color = color });
                }
                if (source.Length == 0) AppendPreformattedText("", para, codeFormat);
            }
            else if (!HasOnlyCaretBreak(node)) AppendInline(node, para, part, format, ref drawingId, layout.BodyImageWidth, layout.ImageHeight);
            body.Append(para);
            return;
        }
        if (node.Name == "input")
        {
            body.Append(new W.Paragraph(Run(SafeEditorDocument.FormControlGlyph(node), new Format())));
            return;
        }
        if (node.Name == "img")
        {
            var para = new W.Paragraph();
            AppendInline(node, para, part, new Format(), ref drawingId, layout.BodyImageWidth, layout.ImageHeight);
            if (para.ChildElements.Count > 0) body.Append(para);
            return;
        }
        if (node.Name == "#text")
        {
            if (!string.IsNullOrWhiteSpace(node.Text))
                body.Append(new W.Paragraph(new W.Run(new W.Text(node.Text) { Space = SpaceProcessingModeValues.Preserve })));
            return;
        }
        foreach (var child in node.Children) AppendBlock(child, body, part, layout, ref drawingId);
    }

    private static void AppendPreformattedText(string source, W.Paragraph paragraph, Format format)
    {
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var offset = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            if (current is not ('\n' or '\t')) continue;
            if (index > offset) paragraph.Append(Run(source[offset..index], format));
            if (current == '\n') paragraph.Append(new W.Run(new W.Break()));
            else paragraph.Append(new W.Run(new W.TabChar()));
            offset = index + 1;
        }
        if (offset < source.Length) paragraph.Append(Run(source[offset..], format));
        if (source.Length == 0) paragraph.Append(Run("", format));
    }

    private static string LegacyListMarker(int value, string style)
    {
        if ((style is "upper-roman" or "lower-roman") && value is >= 1 and <= 3999)
        {
            var result = new System.Text.StringBuilder();
            foreach (var (number, numeral) in new[] { (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),(100,"C"),(90,"XC"),(50,"L"),(40,"XL"),(10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I") })
                while (value >= number) { result.Append(numeral); value -= number; }
            return style == "lower-roman" ? result.ToString().ToLowerInvariant() : result.ToString();
        }
        if ((style is "upper-alpha" or "lower-alpha") && value > 0)
        {
            var result = new System.Text.StringBuilder();
            while (value > 0) { value--; result.Insert(0, (char)((style == "upper-alpha" ? 'A' : 'a') + value % 26)); value /= 26; }
            return result.ToString();
        }
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static W.Paragraph Paragraph(SafeEditorDocument.Node node, string fallbackAlignment = "")
    {
        var para = new W.Paragraph();
        var align = node.Style("text-align");
        if (align.Length == 0) align = fallbackAlignment;
        var justification = align switch
        {
            "center" => W.JustificationValues.Center,
            "right" => W.JustificationValues.Right,
            "justify" => W.JustificationValues.Both,
            _ => W.JustificationValues.Left
        };
        var properties = new W.ParagraphProperties(new W.Justification { Val = justification });
        var indent = node.Style("margin-left");
        if (int.TryParse(indent.TrimEnd('p', 'x'), out var pixels) && pixels is > 0 and <= 240 && indent.EndsWith("px", StringComparison.Ordinal))
            properties.AddChild(new W.Indentation { Left = (pixels * 15).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        para.Append(properties);
        return para;
    }

    private static W.Table BuildTable(SafeEditorDocument.Node node, MainDocumentPart part, WordLayoutSettings layout, ref uint drawingId)
    {
        var color = node.Style("border-color").TrimStart('#');
        var explicitBorder = (color.Length == 6 && color.All(Uri.IsHexDigit)) || node.Style("border-width") is ("1px" or "2px" or "3px" or "4px");
        if (color.Length != 6 || !color.All(Uri.IsHexDigit)) color = layout.DocumentStyle?.BorderColor[1..] ?? "AEB8C6";
        var borderValue = node.Style("border-style") == "none" || (layout.DocumentStyle?.BrowserDefaults == true && !explicitBorder) ? W.BorderValues.Nil : W.BorderValues.Single;
        var pixels = node.Style("border-width") switch { "2px" => 2, "3px" => 3, "4px" => 4, _ => 1 };
        var thickness = (uint)(pixels * 6);
        var borders = new W.TableBorders(
            new W.TopBorder { Val = borderValue, Size = thickness, Color = color },
            new W.LeftBorder { Val = borderValue, Size = thickness, Color = color },
            new W.BottomBorder { Val = borderValue, Size = thickness, Color = color },
            new W.RightBorder { Val = borderValue, Size = thickness, Color = color },
            new W.InsideHorizontalBorder { Val = borderValue, Size = thickness, Color = color },
            new W.InsideVerticalBorder { Val = borderValue, Size = thickness, Color = color });
        var tablePercent = 100.0;
        if (layout.DocumentStyle is not null && TryPercentWidth(node, out var requestedWidth)) tablePercent = requestedWidth;
        var tableProperties = new W.TableProperties(
            new W.TableWidth { Width = ((int)Math.Round(tablePercent * 50)).ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Pct }, borders);
        if (node.Style("table-layout") == "fixed")
            tableProperties.AddChild(new W.TableLayout { Type = W.TableLayoutValues.Fixed });
        if (layout.VerticalPadding is int vertical && layout.HorizontalPadding is int horizontal)
        {
            // Use the Office 2007-compatible left/right margin elements.
            var v = (vertical * 15).ToString(CultureInfo.InvariantCulture);
            var h = (horizontal * 15).ToString(CultureInfo.InvariantCulture);
            tableProperties.AddChild(new W.TableCellMarginDefault(
                $"<w:tblCellMar xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                $"<w:top w:w=\"{v}\" w:type=\"dxa\"/><w:left w:w=\"{h}\" w:type=\"dxa\"/>" +
                $"<w:bottom w:w=\"{v}\" w:type=\"dxa\"/><w:right w:w=\"{h}\" w:type=\"dxa\"/></w:tblCellMar>"));
        }
        var rowNodes = TableRows(node).Where(row => row.Children.Any(c => c.Name is "td" or "th")).ToList();
        var rows = rowNodes.Select(row => row.Children.Where(c => c.Name is "td" or "th").ToList()).ToList();
        if (rows.Count == 0) rows.Add(new List<SafeEditorDocument.Node> { new("td") });
        var columnCount = rows.Max(cells => cells.Sum(CellSpan));
        var widths = ColumnWidths(rows, columnCount, (int)Math.Round(layout.PageWidth * tablePercent / 100));
        var grid = new W.TableGrid();
        foreach (var width in widths)
            grid.Append(new W.GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) });
        var table = new W.Table(tableProperties, grid);
        var initialHeader = true;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var cells = rows[rowIndex];
            var row = new W.TableRow();
            // Only contiguous initial header rows can be repeated by Word.
            var rowProperties = new W.TableRowProperties();
            var missing = columnCount - cells.Sum(CellSpan);
            if (missing > 0) rowProperties.Append(new W.GridAfter { Val = missing });
            rowProperties.Append(new W.CantSplit());
            if (layout.DocumentStyle is not null && rowIndex < rowNodes.Count)
            {
                var height = rowNodes[rowIndex].Style("height");
                if (height.EndsWith("px", StringComparison.Ordinal) && int.TryParse(height[..^2], NumberStyles.None, CultureInfo.InvariantCulture, out var heightPx) && heightPx is >= 1 and <= 1000)
                    rowProperties.AddChild(new W.TableRowHeight { Val = (uint)(heightPx * 15), HeightType = W.HeightRuleValues.AtLeast });
            }
            initialHeader = initialHeader && cells.All(c => c.Name == "th");
            if (initialHeader) rowProperties.Append(new W.TableHeader());
            row.Append(rowProperties);
            var column = 0;
            foreach (var cellNode in cells)
            {
                var span = CellSpan(cellNode);
                var width = 0;
                for (var i = 0; i < span; i++) width += widths[column + i];
                column += span;
                var cell = new W.TableCell();
                var cellProperties = new W.TableCellProperties();
                if (layout.DocumentStyle is null && TryPercentWidth(cellNode, out var percentage))
                    cellProperties.Append(new W.TableCellWidth
                    {
                        Type = W.TableWidthUnitValues.Pct,
                        Width = ((int)Math.Round(percentage * 50)).ToString(CultureInfo.InvariantCulture)
                    });
                else cellProperties.Append(new W.TableCellWidth
                {
                    Type = W.TableWidthUnitValues.Dxa,
                    Width = width.ToString(CultureInfo.InvariantCulture)
                });
                if (span > 1) cellProperties.Append(new W.GridSpan { Val = span });
                var topBorder = cellNode.Style("border-top-style") == "solid" &&
                    cellNode.Style("border-top-width") is ("1px" or "2px" or "3px" or "4px") &&
                    Regex.IsMatch(cellNode.Style("border-top-color"), @"\A#[0-9a-fA-F]{6}\z");
                var borderless = cellNode.Style("border-style") == "none";
                if (borderless || topBorder)
                {
                    var cellBorders = new W.TableCellBorders();
                    if (topBorder)
                        cellBorders.AddChild(new W.TopBorder { Val = W.BorderValues.Single,
                            Size = (uint)((cellNode.Style("border-top-width")[0] - '0') * 6), Color = cellNode.Style("border-top-color")[1..] });
                    else cellBorders.AddChild(new W.TopBorder { Val = W.BorderValues.Nil });
                    if (borderless)
                    {
                        cellBorders.AddChild(new W.LeftBorder { Val = W.BorderValues.Nil });
                        cellBorders.AddChild(new W.BottomBorder { Val = W.BorderValues.Nil });
                        cellBorders.AddChild(new W.RightBorder { Val = W.BorderValues.Nil });
                    }
                    cellProperties.AddChild(cellBorders);
                }

                if (layout.DocumentStyle is { } documentStyle)
                {
                    var fill = cellNode.Style("background-color");
                    if (!Regex.IsMatch(fill, @"\A#[0-9a-fA-F]{6}\z"))
                        fill = cellNode.Name == "th" && !documentStyle.BrowserDefaults ? documentStyle.HeaderColor : "";
                    if (fill.Length > 0) cellProperties.AddChild(new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = fill[1..] });
                }
                cell.Append(cellProperties);
                // The grid and cell padding determine the available image width,
                // including horizontally merged cells. Leave a small border allowance.
                var imageWidth = Math.Clamp((width - (layout.HorizontalPadding ?? 7) * 30 - 30) / 15, 1, 480);
                var format = new Format(Bold: cellNode.Name == "th", Size: layout.TableSize);
                AppendCellContent(cellNode, cell, part, layout, format, imageWidth, ref drawingId);
                row.Append(cell);
            }
            table.Append(row);
        }
        return table;
    }

    private static IEnumerable<SafeEditorDocument.Node> TableRows(SafeEditorDocument.Node table)
    {
        foreach (var child in table.Children)
        {
            if (child.Name == "tr") yield return child;
            else if (child.Name is "thead" or "tbody" or "tfoot")
                foreach (var row in child.Children.Where(c => c.Name == "tr")) yield return row;
        }
    }

    private static int CellSpan(SafeEditorDocument.Node cell) =>
        int.TryParse(cell.Attribute("colspan"), out var span) && span is >= 2 and <= 12 ? span : 1;

    private static int[] ColumnWidths(List<List<SafeEditorDocument.Node>> rows, int count, int pageWidth)
    {
        // Prefer a full unmerged row with explicit width hints; a leading title
        // spanning the table must not define the individual column widths.
        var hint = rows.Where(cells => cells.Count == count && cells.All(c => CellSpan(c) == 1))
            .OrderByDescending(cells => cells.Count(c => TryPercentWidth(c, out _))).FirstOrDefault();
        var weights = new double[count];
        if (hint is not null)
            for (var i = 0; i < count; i++)
                if (TryPercentWidth(hint[i], out var percent)) weights[i] = percent;
        var specified = weights.Sum();
        var unspecified = weights.Count(w => w == 0);
        var fallback = unspecified == 0 ? 0 : specified < 100 ? (100 - specified) / unspecified : 100.0 / count;
        for (var i = 0; i < count; i++) if (weights[i] == 0) weights[i] = fallback;
        var total = weights.Sum();
        // Reserve at least one twip per column; rounding cannot produce a zero-width grid.
        var target = Math.Max(pageWidth, count);
        var result = new int[count];
        var assigned = 0;
        double cumulative = 0;
        for (var i = 0; i < count; i++)
        {
            cumulative += weights[i];
            var boundary = i == count - 1 ? target - count : (int)Math.Round((target - count) * cumulative / total);
            result[i] = 1 + boundary - assigned;
            assigned = boundary;
        }
        return result;
    }

    private static void AppendCellContent(SafeEditorDocument.Node node, W.TableCell cell, MainDocumentPart part,
        WordLayoutSettings layout, Format format, int imageWidth, ref uint drawingId)
    {
        // Direct paragraph blocks stay separately editable instead of concatenating their text.
        // Nested tables retain their text in the containing cell, not as duplicate outer rows.
        var inherited = Merge(format, node);
        W.Paragraph? current = null;
        var children = HasOnlyCaretBreak(node) ? Enumerable.Empty<SafeEditorDocument.Node>() : node.Children;
        foreach (var child in children)
        {
            if (child.Name is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "blockquote")
            {
                current = null;
                var paragraph = Paragraph(child, node.Style("text-align"));
                ApplyCellSpacing(paragraph, layout, Merge(inherited, child));
                if (!HasOnlyCaretBreak(child))
                    AppendInline(child, paragraph, part, inherited, ref drawingId, imageWidth, layout.ImageHeight);
                cell.Append(paragraph);
                continue;
            }
            if (child.Name == "#text" && string.IsNullOrWhiteSpace(child.Text) && current is null) continue;
            if (current is null)
            {
                current = Paragraph(node);
                ApplyCellSpacing(current, layout, inherited);
                cell.Append(current);
            }
            AppendInline(child, current, part, inherited, ref drawingId, imageWidth, layout.ImageHeight);
        }
        if (!cell.Elements<W.Paragraph>().Any())
        {
            var empty = Paragraph(node);
            ApplyCellSpacing(empty, layout, inherited);
            cell.Append(empty);
        }
    }

    // A lone BR in an otherwise empty HTML block is a contenteditable caret
    // placeholder, not two visible lines. Word already supplies a paragraph mark.
    // Do not strip BRs from nonempty blocks, multiple BRs or preformatted code.
    private static bool HasOnlyCaretBreak(SafeEditorDocument.Node node)
    {
        var count = 0;
        foreach (var child in node.Children)
        {
            if (child.Name == "#text" && child.Text.All(c => c is ' ' or '\t' or '\r' or '\n' or '\f')) continue;
            if (child.Name != "br" || ++count > 1) return false;
        }
        return count == 1;
    }

    private static void ApplyCellSpacing(W.Paragraph paragraph, WordLayoutSettings layout, Format format)
    {
        var properties = paragraph.GetFirstChild<W.ParagraphProperties>();
        if (properties is null) return;
        if (layout.CompactSpacing)
            properties.AddChild(new W.SpacingBetweenLines
            { Before = "0", After = "0", Line = ((int)Math.Round((layout.DocumentStyle?.LineHeight ?? 1) * 240)).ToString(CultureInfo.InvariantCulture), LineRule = W.LineSpacingRuleValues.Auto });
        // An empty cell has no text run. Its paragraph mark must use the table
        // font size too, or Word may use the larger body font for the entire row.
        if (layout.TableSize.HasValue && format.Size is double size)
        {
            var halfPoints = WordLayoutSettings.HalfPoints(size);
            properties.AddChild(new W.ParagraphMarkRunProperties(
                new W.FontSize { Val = halfPoints },
                new W.FontSizeComplexScript { Val = halfPoints }));
        }
    }

    private static bool TryPercentWidth(SafeEditorDocument.Node cell, out double percentage)
    {
        percentage = 0;
        var width = cell.Style("width");
        return width.EndsWith('%') &&
            double.TryParse(width[..^1], System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out percentage) &&
            percentage is >= 1 and <= 100;
    }

    private static void AppendInline(SafeEditorDocument.Node node, W.Paragraph para, MainDocumentPart part,
        Format parent, ref uint drawingId, int maxImageWidth = 560, int maxImageHeight = 1200)
    {
        if (node.Name == "#text")
        {
            if (node.Text.Length > 0) para.Append(Run(node.Text, parent));
            return;
        }
        if (node.Name == "input") { para.Append(Run(SafeEditorDocument.FormControlGlyph(node), parent)); return; }
        if (node.Name == "br") { para.Append(new W.Run(new W.Break())); return; }
        if (node.Name == "img")
        {
            var image = ImageRun(node, part, ref drawingId, maxImageWidth, maxImageHeight);
            if (image is not null) para.Append(image);
            return;
        }
        var next = Merge(parent, node);
        if (node.Name == "a" && SafeEditorDocument.TryLink(node.Attribute("href"), out var url))
        {
            // A relative editor link has no trusted export origin; keep readable text
            // rather than attempting an invalid external Open XML relationship.
            // Absolute HTTP(S) and mailto references are safe external relationships.
            if (Uri.TryCreate(url, UriKind.Absolute, out var target) &&
                target.Scheme is "http" or "https" or "mailto")
            {
                var rel = part.AddHyperlinkRelationship(target, true);
                var hyperlink = new W.Hyperlink { Id = rel.Id, History = true };
                hyperlink.Append(Run(node.PlainText(), next with { Underline = true, Color = "1D4ED8" }));
                para.Append(hyperlink);
            }
            else
            {
                foreach (var child in node.Children)
                    AppendInline(child, para, part, next, ref drawingId, maxImageWidth, maxImageHeight);
            }
            return;
        }
        foreach (var child in node.Children) AppendInline(child, para, part, next, ref drawingId, maxImageWidth, maxImageHeight);
    }

    private static Format Merge(Format current, SafeEditorDocument.Node node)
    {
        var cssSize = node.Style("font-size");
        int? size = null;
        if (cssSize.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(cssSize[..^2], out var pixels) && pixels is >= 8 and <= 72)
            size = (int)Math.Round(pixels * 0.75);
        else if (cssSize.EndsWith("pt", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(cssSize[..^2], NumberStyles.None, CultureInfo.InvariantCulture, out var points) && points is >= 6 and <= 54)
            size = points;
        var font = node.Style("font-family").Trim(' ', '\'', '"');
        var color = node.Style("color");
        var highlight = node.Style("background-color");
        return current with
        {
            Bold = current.Bold || node.Name is ("b" or "strong" or "th") || node.Style("font-weight") is ("bold" or "600" or "700" or "800" or "900"),
            Italic = current.Italic || node.Name is ("i" or "em") || node.Style("font-style") == "italic",
            Underline = current.Underline || node.Name == "u" || node.Style("text-decoration-line").Split(' ').Contains("underline"),
            Strike = current.Strike || node.Name is ("s" or "strike" or "del") || node.Style("text-decoration-line").Split(' ').Contains("line-through"),
            Script = node.Name is "sup" or "sub" ? node.Name : current.Script,
            Font = font.Length > 0 ? font : current.Font,
            Size = size ?? current.Size,
            Color = Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") ? color[1..] : current.Color,
            Highlight = Regex.IsMatch(highlight, "^#[0-9a-fA-F]{6}$") ? highlight[1..] : current.Highlight
        };
    }

    private static W.Run Run(string text, Format format)
    {
        var properties = new W.RunProperties();
        if (format.Bold) properties.AddChild(new W.Bold());
        if (format.Italic) properties.AddChild(new W.Italic());
        if (format.Underline) properties.AddChild(new W.Underline { Val = W.UnderlineValues.Single });
        if (format.Strike) properties.AddChild(new W.Strike());
        if (format.Script is "sup" or "sub")
            properties.AddChild(new W.VerticalTextAlignment
            {
                Val = format.Script == "sup" ? W.VerticalPositionValues.Superscript : W.VerticalPositionValues.Subscript
            });
        if (!string.IsNullOrEmpty(format.Font)) properties.AddChild(new W.RunFonts
        { Ascii = format.Font, HighAnsi = format.Font, EastAsia = format.Font });
        if (format.Size is > 0)
        {
            var size = WordLayoutSettings.HalfPoints(format.Size.Value);
            properties.AddChild(new W.FontSize { Val = size });
            properties.AddChild(new W.FontSizeComplexScript { Val = size });
        }
        if (!string.IsNullOrEmpty(format.Color)) properties.AddChild(new W.Color { Val = format.Color });
        if (!string.IsNullOrEmpty(format.Highlight)) properties.AddChild(new W.Shading { Fill = format.Highlight });
        return new W.Run(properties, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static W.Run? ImageRun(SafeEditorDocument.Node node, MainDocumentPart main, ref uint drawingId,
        int maxWidth = 560, int maxHeight = 1200)
    {
        if (!SafeEditorDocument.TryImage(node, out var mime, out var bytes)) return null;
        var part = main.AddImagePart(mime);
        using (var image = new MemoryStream(bytes, writable: false)) part.FeedData(image);
        var (sourceWidth, sourceHeight) = ImageSize(bytes, mime);
        // If the editor saved an image without a width attribute, preserve its
        // natural pixel width instead of the historical fixed 480px fallback.
        // Clamp only to the page/table's available width, never upscale thumbnails.
        var width = Math.Max(1, sourceWidth);
        if (int.TryParse(node.Attribute("width"), out var definedWidth) && definedWidth is >= 24 and <= 2500)
            width = definedWidth;
        width = Math.Clamp(width, 1, maxWidth);
        var scaledHeight = width * (double)sourceHeight / Math.Max(1, sourceWidth);
        // Never force tiny images to a 20px height: it distorts 1x1 icons and signatures.
        // If height exceeds the printable limit, scale *both* axes proportionally.
        if (scaledHeight > maxHeight)
        {
            width = Math.Max(1, (int)Math.Floor(width * maxHeight / scaledHeight));
            scaledHeight = width * (double)sourceHeight / Math.Max(1, sourceWidth);
        }
        var height = Math.Clamp((int)Math.Round(scaledHeight), 1, maxHeight);
        var cx = (long)width * 9525;
        var cy = (long)height * 9525;
        var id = drawingId++;
        var altText = node.Attribute("alt");
        if (altText.Length > 200) altText = altText[..200];
        var picture = new PIC.Picture(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties { Id = id, Name = "Image", Description = altText },
                new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(
                new A.Blip { Embed = main.GetIdOfPart(part) },
                new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(
                new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
        var graphic = new A.Graphic(new A.GraphicData(picture)
        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" });
        var inline = new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.DocProperties { Id = id, Name = "Azunt image", Description = altText },
            graphic)
        { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U };
        return new W.Run(new W.Drawing(inline));
    }

    private static (int Width, int Height) ImageSize(byte[] bytes, string mime)
    {
        if (mime == "image/png" && bytes.Length > 24)
        {
            var w = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var h = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            if (w is > 0 and < 100000 && h is > 0 and < 100000) return (w, h);
        }
        if (mime == "image/jpeg")
        {
            var i = 2;
            while (i + 9 < bytes.Length)
            {
                if (bytes[i] != 0xff) { i++; continue; }
                var marker = bytes[i + 1];
                if (marker is 0xd8 or 0xd9 or 0x01 || marker is >= 0xd0 and <= 0xd7) { i += 2; continue; }
                var segment = (bytes[i + 2] << 8) | bytes[i + 3];
                if (segment < 2 || i + 2 + segment > bytes.Length) break;
                if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3)
                {
                    var h = (bytes[i + 5] << 8) | bytes[i + 6];
                    var w = (bytes[i + 7] << 8) | bytes[i + 8];
                    if (w > 0 && h > 0) return (w, h);
                }
                i += 2 + segment;
            }
        }
        return (4, 3);
    }
}
