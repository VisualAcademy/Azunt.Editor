using Azunt.Editor.Export.Html;
using Azunt.Editor.Export.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Azunt.Editor.Export.Word;

internal static class DocxDocumentExporter
{
    private readonly record struct Format(bool Bold = false, bool Italic = false, bool Underline = false,
        bool Strike = false, string? Script = null, string? Font = null, int? Size = null,
        string? Color = null, string? Highlight = null);

    public static byte[] Export(SafeEditorDocument document, EditorExportOptions settings)
    {
        using var buffer = new MemoryStream();
        using (var package = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart();
            var body = new W.Body();
            main.Document = new W.Document(body);
            uint drawingId = 1;
            foreach (var child in document.Root.Children) AppendBlock(child, body, main, ref drawingId);
            if (!body.Elements<W.Paragraph>().Any() && !body.Elements<W.Table>().Any()) body.Append(new W.Paragraph());
            var landscape = settings.Orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase);
            var size = settings.PaperSize switch
            {
                "Letter" => (Width: 12240U, Height: 15840U),
                "Legal" => (Width: 12240U, Height: 20160U),
                _ => (Width: 11906U, Height: 16838U)
            };
            var margin = settings.Margin switch { "narrow" => 454, "wide" => 1417, _ => 907 };
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

    private static void AppendBlock(SafeEditorDocument.Node node, W.Body body, MainDocumentPart part, ref uint drawingId)
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
            body.Append(BuildTable(node, part, ref drawingId));
            return;
        }
        if (node.Name is "ul" or "ol")
        {
            var i = 0;
            foreach (var item in node.Children.Where(c => c.Name == "li"))
            {
                i++;
                var para = Paragraph(item);
                para.Append(new W.Run(new W.Text(node.Name == "ul" ? "• " : $"{i}. ")));
                AppendInline(item, para, part, new Format(), ref drawingId);
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
            if (node.Name == "pre")
            {
                // Code is stored as plain text; do not generate syntax-markup in the file.
                // Word requires explicit break/tab elements: newlines inside w:t are not rendered.
                var properties = para.GetFirstChild<W.ParagraphProperties>();
                properties?.Append(new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = "F1F5F9" });
                var code = node.Children.FirstOrDefault(child => child.Name == "code");
                AppendPreformattedText(code?.PlainText() ?? node.PlainText(), para,
                    new Format(Font: "Consolas", Size: 10, Color: "0F172A"));
            }
            else AppendInline(node, para, part, format, ref drawingId);
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
            AppendInline(node, para, part, new Format(), ref drawingId);
            if (para.ChildElements.Count > 0) body.Append(para);
            return;
        }
        if (node.Name == "#text")
        {
            if (!string.IsNullOrWhiteSpace(node.Text))
                body.Append(new W.Paragraph(new W.Run(new W.Text(node.Text) { Space = SpaceProcessingModeValues.Preserve })));
            return;
        }
        foreach (var child in node.Children) AppendBlock(child, body, part, ref drawingId);
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

    private static W.Paragraph Paragraph(SafeEditorDocument.Node node)
    {
        var para = new W.Paragraph();
        var align = node.Style("text-align");
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
            properties.Append(new W.Indentation { Left = (pixels * 15).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        para.Append(properties);
        return para;
    }

    private static W.Table BuildTable(SafeEditorDocument.Node node, MainDocumentPart part, ref uint drawingId)
    {
        var color = node.Style("border-color").TrimStart('#');
        if (color.Length != 6 || !color.All(Uri.IsHexDigit)) color = "AEB8C6";
        var pixels = node.Style("border-width") switch { "2px" => 2, "3px" => 3, "4px" => 4, _ => 1 };
        var thickness = (uint)(pixels * 6);
        var borders = new W.TableBorders(
            new W.TopBorder { Val = W.BorderValues.Single, Size = thickness, Color = color },
            new W.LeftBorder { Val = W.BorderValues.Single, Size = thickness, Color = color },
            new W.BottomBorder { Val = W.BorderValues.Single, Size = thickness, Color = color },
            new W.RightBorder { Val = W.BorderValues.Single, Size = thickness, Color = color },
            new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = thickness, Color = color },
            new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = thickness, Color = color });
        var tableProperties = new W.TableProperties(
            new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct }, borders);
        if (node.Style("table-layout") == "fixed")
            tableProperties.Append(new W.TableLayout { Type = W.TableLayoutValues.Fixed });
        var table = new W.Table(tableProperties);
        foreach (var rowNode in node.DescendantsAndSelf().Where(e => e.Name == "tr"))
        {
            var row = new W.TableRow();
            var cells = rowNode.Children.Where(e => e.Name is "td" or "th").ToList();
            // Keep each row together. Repeat semantic header rows on subsequent pages.
            var rowProperties = new W.TableRowProperties(new W.CantSplit());
            if (cells.Count > 0 && cells.All(c => c.Name == "th"))
                rowProperties.Append(new W.TableHeader());
            row.Append(rowProperties);
            var logicalColumns = cells.Sum(c => int.TryParse(c.Attribute("colspan"), out var span) && span is >= 2 and <= 12 ? span : 1);
            var cellImageMaxWidth = Math.Clamp(640 / Math.Max(logicalColumns, 1), 120, 480);
            foreach (var cellNode in cells)
            {
                var cell = new W.TableCell();
                var cellProperties = new W.TableCellProperties();
                if (TryPercentWidth(cellNode, out var percentage))
                    cellProperties.Append(new W.TableCellWidth
                    {
                        Type = W.TableWidthUnitValues.Pct,
                        Width = ((int)Math.Round(percentage * 50)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
                if (int.TryParse(cellNode.Attribute("colspan"), out var span) && span is >= 2 and <= 12)
                    cellProperties.Append(new W.GridSpan { Val = span });
                if (cellProperties.HasChildren) cell.Append(cellProperties);
                var para = Paragraph(cellNode);
                AppendInline(cellNode, para, part,
                    cellNode.Name == "th" ? new Format(Bold: true) : new Format(), ref drawingId, cellImageMaxWidth);
                cell.Append(para);
                row.Append(cell);
            }
            table.Append(row);
        }
        return table;
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
        Format parent, ref uint drawingId, int maxImageWidth = 560)
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
            var image = ImageRun(node, part, ref drawingId, maxImageWidth);
            if (image is not null) para.Append(image);
            return;
        }
        var next = Merge(parent, node);
        if (node.Name == "a" && SafeEditorDocument.TryLink(node.Attribute("href"), out var url))
        {
            var rel = part.AddHyperlinkRelationship(new Uri(url), true);
            var hyperlink = new W.Hyperlink { Id = rel.Id, History = true };
            var text = node.PlainText();
            hyperlink.Append(Run(text, next with { Underline = true, Color = "1D4ED8" }));
            para.Append(hyperlink);
            return;
        }
        foreach (var child in node.Children) AppendInline(child, para, part, next, ref drawingId, maxImageWidth);
    }

    private static Format Merge(Format current, SafeEditorDocument.Node node)
    {
        var cssSize = node.Style("font-size");
        int? size = null;
        if (cssSize.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(cssSize[..^2], out var pixels) && pixels is >= 8 and <= 72)
            size = (int)Math.Round(pixels * 0.75);
        var font = node.Style("font-family").Trim(' ', '\'', '"');
        var color = node.Style("color");
        var highlight = node.Style("background-color");
        return current with
        {
            Bold = current.Bold || node.Name is "b" or "strong" or "th",
            Italic = current.Italic || node.Name is "i" or "em",
            Underline = current.Underline || node.Name == "u",
            Strike = current.Strike || node.Name is "s" or "strike" or "del",
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
        if (format.Bold) properties.Append(new W.Bold());
        if (format.Italic) properties.Append(new W.Italic());
        if (format.Underline) properties.Append(new W.Underline { Val = W.UnderlineValues.Single });
        if (format.Strike) properties.Append(new W.Strike());
        if (format.Script is "sup" or "sub")
            properties.Append(new W.VerticalTextAlignment
            {
                Val = format.Script == "sup" ? W.VerticalPositionValues.Superscript : W.VerticalPositionValues.Subscript
            });
        if (!string.IsNullOrEmpty(format.Font)) properties.Append(new W.RunFonts
        { Ascii = format.Font, HighAnsi = format.Font, EastAsia = format.Font });
        if (format.Size is > 0) properties.Append(new W.FontSize { Val = (format.Size.Value * 2).ToString() });
        if (!string.IsNullOrEmpty(format.Color)) properties.Append(new W.Color { Val = format.Color });
        if (!string.IsNullOrEmpty(format.Highlight)) properties.Append(new W.Shading { Fill = format.Highlight });
        return new W.Run(properties, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static W.Run? ImageRun(SafeEditorDocument.Node node, MainDocumentPart main, ref uint drawingId,
        int maxWidth = 560)
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
        width = Math.Min(width, maxWidth);
        var height = Math.Clamp((int)Math.Round(width * (double)sourceHeight / Math.Max(1, sourceWidth)), 20, 1200);
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
