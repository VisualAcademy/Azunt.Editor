using Azunt.Editor.Export.Html;
using Azunt.Editor.Export.Models;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Azunt.Editor.Export.Excel;

/// <summary>
/// Converts editor paragraphs and tables to editable spreadsheet cells and embeds
/// supported inline PNG/JPEG images as anchored Excel drawings.
/// </summary>
internal static class XlsxDocumentExporter
{
    private const uint MaxRows = 50_000;
    private const int MaxImages = 60;
    private const long MaxCombinedImageBytes = 30L * 1024 * 1024;
    private const int PixelsPerReservedRow = 21;

    public static byte[] Export(SafeEditorDocument document, EditorExportOptions options)
    {
        using var buffer = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = package.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook();
            // All authored content remains editable, with a small, bounded style palette.
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new S.SheetData();
            worksheetPart.Worksheet = new S.Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
            sheets.Append(new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Report"
            });

            // Collect bounded text widths while streaming rows. The Columns element
            // is inserted before SheetData to satisfy SpreadsheetML ordering.
            var textWidths = new double[100];
            var resizedWidths = new double[100];
            uint rowNumber = 0;
            uint drawingId = 1;
            long totalImageBytes = 0;
            DrawingsPart? drawingsPart = null;
            var mergedRanges = new List<string>();
            var columnWidths = new double[100];
            var tableRows = new List<(S.Row Row, List<(int Column, int Span, string Text)> Cells)>();

            foreach (var node in document.Root.Children) WriteBlock(node);
            if (rowNumber == 0) AppendRow(["Document"]);

            var columns = new S.Columns();
            for (var i = 0; i < textWidths.Length; i++)
            {
                if (textWidths[i] <= 0 && resizedWidths[i] <= 0) continue;
                columnWidths[i] = Math.Clamp(Math.Max(textWidths[i] + 2.5, resizedWidths[i]), 14.0, 58.0);
                columns.Append(new S.Column
                {
                    Min = (uint)i + 1,
                    Max = (uint)i + 1,
                    Width = columnWidths[i],
                    CustomWidth = true
                });
            }
            if (columns.HasChildren)
                worksheetPart.Worksheet.InsertBefore(columns, sheetData);

            // Calculate wrap heights after all column widths are known.
            foreach (var (row, cells) in tableRows)
            {
                var lines = 1;
                foreach (var (column, span, text) in cells)
                {
                    var width = Enumerable.Range(column, span).Sum(i => columnWidths[i] > 0 ? columnWidths[i] : 14);
                    var capacity = Math.Max(1, (int)(width - 2));
                    lines = Math.Max(lines, text.Split('\n').Sum(line => Math.Max(1,
                        (int)Math.Ceiling((double)line.Sum(ch => ch > 0xFF ? 2 : 1) / capacity))));
                }
                row.Height = Math.Min(409, Math.Max(row.Height?.Value ?? 0, 5 + 15.0 * lines));
                row.CustomHeight = true;
            }

            if (mergedRanges.Count > 0)
            {
                var merges = new S.MergeCells();
                foreach (var range in mergedRanges) merges.Append(new S.MergeCell { Reference = range });
                worksheetPart.Worksheet.Append(merges);
            }
            if (drawingsPart is not null)
            {
                // Worksheet drawing references must follow sheetData in SpreadsheetML.
                worksheetPart.Worksheet.Append(new S.Drawing
                {
                    Id = worksheetPart.GetIdOfPart(drawingsPart)
                });
                drawingsPart.WorksheetDrawing.Save();
            }
            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();

            void WriteBlock(SafeEditorDocument.Node node)
            {
                if (node.Name == "table")
                {
                    // Only this table's rows: nested tables stay inside their containing cell.
                    var rows = TableRows(node).ToList();
                    if (rows.Count == 0) return;
                    CaptureColumnWidths(rows[0]);
                    var headerTypes = new Dictionary<int, string>();
                    var first = true;
                    foreach (var tr in rows)
                    {
                        var cells = tr.Children.Where(n => n.Name is "td" or "th").ToList();
                        if (cells.Count == 0) continue;
                        var header = cells.All(c => c.Name == "th");
                        if (rowNumber >= MaxRows) throw new InvalidOperationException("Excel row limit for this export was exceeded.");
                        var row = new S.Row { RowIndex = ++rowNumber };
                        var measured = new List<(int Column, int Span, string Text)>();
                        var column = 0;
                        var imageHeight = 0;
                        foreach (var cell in cells)
                        {
                            var span = int.TryParse(cell.Attribute("colspan"), out var parsed) && parsed is >= 2 and <= 12 ? parsed : 1;
                            if (column + span > 100) throw new InvalidOperationException("Excel table exceeds the 100-column export limit.");
                            var raw = TableCellText(cell);
                            if (raw.Length > 32767) throw new InvalidOperationException("An Excel table cell exceeds 32,767 characters.");
                            var text = Normalize(raw);
                            if (first && header && span == 1)
                            {
                                if (options.SpreadsheetDateColumns.Any(h => string.Equals(h, text, StringComparison.OrdinalIgnoreCase))) headerTypes[column] = "date";
                                else if (options.SpreadsheetNumberColumns.Any(h => string.Equals(h, text, StringComparison.OrdinalIgnoreCase))) headerTypes[column] = "number";
                            }
                            var style = header ? 1U : 3U;
                            var value = TextCell(column, text, style);
                            if (!header && span == 1 && headerTypes.TryGetValue(column, out var type))
                            {
                                if (type == "date" && TryDate(text, options.SpreadsheetCulture, out var serial))
                                    {
                                    value = NumberCell(column, serial.ToString("R", CultureInfo.InvariantCulture), 4U);
                                    resizedWidths[column] = Math.Max(resizedWidths[column], 23);
                                }
                                else if (type == "number" && TryNumber(text, out var number))
                                    value = NumberCell(column, number, 5U);
                            }
                            row.Append(value);
                            // Keep border formatting in the covered cells too.
                            for (var offset = 1; offset < span; offset++) row.Append(TextCell(column + offset, "", style));
                            if (span > 1)
                                mergedRanges.Add($"{ColumnName(column)}{rowNumber}:{ColumnName(column + span - 1)}{rowNumber}");
                            var longest = text.Split('\n').Select(line => line.Sum(ch => ch > 0xFF ? 2 : 1)).DefaultIfEmpty(0).Max();
                            for (var offset = 0; offset < span; offset++)
                                textWidths[column + offset] = Math.Max(textWidths[column + offset], Math.Min(55, Math.Ceiling((double)longest / span)));
                            measured.Add((column, span, text));
                            // Images inside tables must not insert artificial data rows.
                            var cellImageHeight = 0;
                            foreach (var image in cell.DescendantsAndSelf().Where(n => n.Name == "img"))
                                cellImageHeight += AddImage(image, rowNumber - 1, column, cellImageHeight);
                            if (cellImageHeight > 530)
                                throw new InvalidOperationException("Images in one Excel cell exceed the supported row height.");
                            imageHeight = Math.Max(imageHeight, cellImageHeight);
                            column += span;
                        }
                        if (imageHeight > 0) { row.Height = Math.Min(409, imageHeight * .75 + 20); row.CustomHeight = true; }
                        sheetData.Append(row);
                        tableRows.Add((row, measured));
                        first = false;
                    }
                    AppendRow([""]);
                    return;
                }

                if (node.Name == "pre")
                {
                    // One spreadsheet row per source line, preserving indentation and
                    // all text. Never place 100+ lines in a height-limited Excel cell.
                    var code = node.Children.FirstOrDefault(child => child.Name == "code");
                    var source = (code?.PlainText() ?? node.PlainText())
                        .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                    foreach (var line in source.Split('\n'))
                    {
                        if (line.Length == 0) { AppendRow([""], 2U); continue; }
                        for (var offset = 0; offset < line.Length;)
                        {
                            var count = Math.Min(32767, line.Length - offset);
                            // UTF-16 surrogate pairs may not be split between two cells.
                            // An unpaired surrogate is not valid XML and is normalized.
                            if (count > 0 && offset + count < line.Length &&
                                char.IsHighSurrogate(line[offset + count - 1]) &&
                                char.IsLowSurrogate(line[offset + count])) count--;
                            AppendRow([NormalizeXmlText(line.Substring(offset, count))], 2U);
                            offset += count;
                        }
                    }
                    return;
                }
                if (node.Name is "h1" or "h2" or "h3" or "h4" or "p" or "li" or "blockquote")
                {
                    WriteFlow(node);
                    return;
                }
                if (node.Name == "input")
                {
                    AppendRow([SafeEditorDocument.FormControlGlyph(node)]);
                    return;
                }
                if (node.Name == "img")
                {
                    var height = AddImage(node, rowNumber, 0);
                    ReserveImageRows(height);
                    return;
                }
                if (node.Name == "#text")
                {
                    var value = Normalize(node.Text);
                    if (value.Length > 0) AppendRow([value]);
                    return;
                }
                foreach (var child in node.Children) WriteBlock(child);
            }

            void CaptureColumnWidths(SafeEditorDocument.Node firstRow)
            {
                var cells = firstRow.Children.Where(c => c.Name is "td" or "th").ToList();
                if (cells.Count is < 2 or > 100 || cells.Any(c => c.Attribute("colspan").Length != 0)) return;
                var percentages = new List<double>();
                foreach (var cell in cells)
                {
                    var width = cell.Style("width");
                    if (!width.EndsWith('%') ||
                        !double.TryParse(width[..^1], NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out var value) || value is < 1 or > 100) return;
                    percentages.Add(value);
                }
                var sum = percentages.Sum();
                if (sum is < 95 or > 105) return;
                for (var index = 0; index < percentages.Count; index++)
                    resizedWidths[index] = Math.Max(resizedWidths[index],
                        Math.Clamp(60 * percentages[index] / sum, 10, 58));
            }

            void WriteFlow(SafeEditorDocument.Node node)
            {
                var currentText = new StringBuilder();
                Visit(node);
                FlushText();

                void Visit(SafeEditorDocument.Node item)
                {
                    switch (item.Name)
                    {
                        case "#text":
                            currentText.Append(item.Text);
                            break;
                        case "img":
                            FlushText();
                            var height = AddImage(item, rowNumber, 0);
                            ReserveImageRows(height);
                            break;
                        case "input":
                            currentText.Append(SafeEditorDocument.FormControlGlyph(item));
                            break;
                        case "br":
                            currentText.Append('\n');
                            break;
                        default:
                            foreach (var child in item.Children) Visit(child);
                            break;
                    }
                }
                void FlushText()
                {
                    var value = Normalize(currentText.ToString());
                    if (value.Length > 0) AppendRow([value]);
                    currentText.Clear();
                }
            }

            int AddImage(SafeEditorDocument.Node node, uint zeroBasedRow, int column, int pixelOffset = 0)
            {
                if (!SafeEditorDocument.TryImage(node, out var mime, out var data)) return 0;
                if (drawingId > MaxImages || totalImageBytes + data.Length > MaxCombinedImageBytes)
                    throw new InvalidOperationException("The XLSX image count or total image size limit was exceeded.");
                totalImageBytes += data.Length;

                drawingsPart ??= worksheetPart.AddNewPart<DrawingsPart>();
                drawingsPart.WorksheetDrawing ??= new Xdr.WorksheetDrawing();

                var imagePart = drawingsPart.AddImagePart(mime);
                using (var imageStream = new MemoryStream(data, writable: false))
                    imagePart.FeedData(imageStream);

                var (sourceWidth, sourceHeight) = GetImageDimensions(data, mime);
                // Native insertion has no width attribute. Keep its original pixel
                // dimensions unless the user explicitly resized the image.
                var width = Math.Max(1, sourceWidth);
                if (int.TryParse(node.Attribute("width"), out var requested) && requested is >= 24 and <= 2500)
                    width = requested;
                width = Math.Clamp(width, 24, 600);
                var height = Math.Clamp((int)Math.Round((double)width * sourceHeight / Math.Max(sourceWidth, 1)), 1, 450);
                // Keep aspect ratio when limiting very tall images.
                if ((double)width * sourceHeight / Math.Max(sourceWidth, 1) > 450)
                    width = Math.Max(1, (int)Math.Round(450.0 * sourceWidth / Math.Max(sourceHeight, 1)));

                const long emuPerPixel = 9525L;
                var cx = (long)width * emuPerPixel;
                var cy = (long)height * emuPerPixel;
                var id = drawingId++;
                var relId = drawingsPart.GetIdOfPart(imagePart);
                var picture = new Xdr.Picture(
                    new Xdr.NonVisualPictureProperties(
                        new Xdr.NonVisualDrawingProperties
                        {
                            Id = id,
                            Name = $"Azunt image {id}",
                            Description = node.Attribute("alt")[..Math.Min(200, node.Attribute("alt").Length)]
                        },
                        new Xdr.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true })),
                    new Xdr.BlipFill(
                        new A.Blip { Embed = relId },
                        new A.Stretch(new A.FillRectangle())),
                    new Xdr.ShapeProperties(
                        new A.Transform2D(
                            new A.Offset { X = 0L, Y = 0L },
                            new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList())
                        { Preset = A.ShapeTypeValues.Rectangle }));
                drawingsPart.WorksheetDrawing.Append(new Xdr.OneCellAnchor(
                    new Xdr.FromMarker
                    {
                        ColumnId = new Xdr.ColumnId(column.ToString(CultureInfo.InvariantCulture)),
                        ColumnOffset = new Xdr.ColumnOffset("0"),
                        RowId = new Xdr.RowId(zeroBasedRow.ToString(CultureInfo.InvariantCulture)),
                        RowOffset = new Xdr.RowOffset(((long)pixelOffset * emuPerPixel).ToString(CultureInfo.InvariantCulture))
                    },
                    new Xdr.Extent { Cx = cx, Cy = cy },
                    picture,
                    new Xdr.ClientData()));
                return height;
            }

            void ReserveImageRows(int height)
            {
                if (height <= 0) return;
                var count = Math.Max(1, (int)Math.Ceiling((double)height / PixelsPerReservedRow));
                for (var i = 0; i < count; i++) AppendRow([]);
                AppendRow([""]);
            }

            S.Cell TextCell(int column, string text, uint style) => new S.Cell
            {
                CellReference = ColumnName(column) + rowNumber.ToString(CultureInfo.InvariantCulture),
                DataType = S.CellValues.InlineString, StyleIndex = style,
                InlineString = new S.InlineString(new S.Text(text) { Space = SpaceProcessingModeValues.Preserve })
            };

            S.Cell NumberCell(int column, string value, uint style) => new S.Cell
            {
                CellReference = ColumnName(column) + rowNumber.ToString(CultureInfo.InvariantCulture),
                DataType = S.CellValues.Number, StyleIndex = style, CellValue = new S.CellValue(value)
            };

            void AppendRow(string[] cells, uint styleIndex = 0U)
            {
                if (rowNumber >= MaxRows) throw new InvalidOperationException("Excel row limit for this export was exceeded.");
                rowNumber++;
                var row = new S.Row { RowIndex = rowNumber };
                var maxLines = 1;
                for (int col = 0; col < cells.Length; col++)
                {
                    var text = cells[col];
                    var lines = text.Split('\n');
                    maxLines = Math.Max(maxLines, lines.Length);
                    // Count wide characters more generously. Keep all widths bounded.
                    var longest = lines.Select(line => line.Sum(ch => ch > 0xFF ? 2 : 1)).DefaultIfEmpty(0).Max();
                    textWidths[col] = Math.Max(textWidths[col], Math.Min(55, longest));
                    // Inline strings also protect leading '=' and '+' from formula execution.
                    var cell = new S.Cell
                    {
                        CellReference = ColumnName(col) + rowNumber.ToString(CultureInfo.InvariantCulture),
                        DataType = S.CellValues.InlineString,
                        InlineString = new S.InlineString(new S.Text(cells[col])
                        { Space = SpaceProcessingModeValues.Preserve })
                    };
                    if (styleIndex != 0U) cell.StyleIndex = styleIndex;
                    row.Append(cell);
                }
                if (maxLines > 1)
                {
                    row.Height = Math.Min(90.0, 15.0 * maxLines);
                    row.CustomHeight = true;
                }
                sheetData.Append(row);
            }
        }
        return buffer.ToArray();
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

    private static string TableCellText(SafeEditorDocument.Node node)
    {
        var output = new StringBuilder();
        Visit(node);
        return output.ToString().Trim();
        void Visit(SafeEditorDocument.Node item)
        {
            if (item.Name == "#text")
                output.Append(Regex.Replace(item.Text, @"[\t\r\n ]+", " "));
            else if (item.Name == "br") output.Append('\n');
            else if (item.Name == "input") output.Append(SafeEditorDocument.FormControlGlyph(item));
            else
            {
                var block = item.Name is "p" or "div" or "li" or "tr";
                if (block && output.Length > 0 && output[^1] != '\n') output.Append('\n');
                foreach (var child in item.Children) Visit(child);
                if (block && output.Length > 0 && output[^1] != '\n') output.Append('\n');
                if (item.Name is "td" or "th" && !ReferenceEquals(item, node)) output.Append(' ');
            }
        }
    }

    private static bool TryDate(string text, string cultureName, out double serial)
    {
        serial = 0;
        var culture = string.Equals(cultureName, "en-US", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.InvariantCulture;
        // Only explicit, four-digit-year date formats; no ambiguous free-form parsing.
        string[] formats = culture.Name == "en-US"
            ? ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss",
                "M/d/yyyy", "M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt", "M/d/yyyy H:mm:ss", "M/d/yyyy H:mm"]
            : ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];
        if (!DateTime.TryParseExact(text, formats, culture, DateTimeStyles.AllowWhiteSpaces, out var date) ||
            date < new DateTime(1900, 3, 1)) return false;
        serial = date.ToOADate();
        return true;
    }

    private static bool TryNumber(string text, out string number)
    {
        number = "";
        // Do not coerce identifiers, leading zeroes, formula-like text, or values beyond Excel precision.
        if (!Regex.IsMatch(text, @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$", RegexOptions.CultureInvariant) ||
            text.Count(char.IsDigit) > 15 ||
            !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)) return false;
        number = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static S.Stylesheet CreateStylesheet()
    {
        // Keep style 0/1/2 IDs stable for existing exports; add body/date/number formats.
        S.CellFormat TableFormat(uint font, uint fill, uint number = 0U) => new S.CellFormat(
            new S.Alignment { Vertical = S.VerticalAlignmentValues.Center, WrapText = true })
        {
            FontId = font, FillId = fill, BorderId = 1U, NumberFormatId = number,
            ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true,
            ApplyNumberFormat = number != 0U
        };
        return new S.Stylesheet(
            new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164U, FormatCode = "yyyy-mm-dd hh:mm:ss" }) { Count = 1U },
            new S.Fonts(
                new S.Font(),
                new S.Font(new S.Bold()),
                new S.Font(new S.FontName { Val = "Consolas" }, new S.FontSize { Val = 10D })),
            new S.Fills(
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }),
                new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = "FFF1F5F9" },
                    new S.BackgroundColor { Indexed = 64U }) { PatternType = S.PatternValues.Solid }),
                new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = "FFE2E8F0" },
                    new S.BackgroundColor { Indexed = 64U }) { PatternType = S.PatternValues.Solid })),
            new S.Borders(new S.Border(), new S.Border(
                new S.LeftBorder(new S.Color { Rgb = "FFD1D5DB" }) { Style = S.BorderStyleValues.Thin },
                new S.RightBorder(new S.Color { Rgb = "FFD1D5DB" }) { Style = S.BorderStyleValues.Thin },
                new S.TopBorder(new S.Color { Rgb = "FFD1D5DB" }) { Style = S.BorderStyleValues.Thin },
                new S.BottomBorder(new S.Color { Rgb = "FFD1D5DB" }) { Style = S.BorderStyleValues.Thin },
                new S.DiagonalBorder())),
            new S.CellStyleFormats(new S.CellFormat()),
            new S.CellFormats(
                new S.CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U },
                TableFormat(1U, 3U),
                new S.CellFormat { FontId = 2U, FillId = 2U, BorderId = 0U, ApplyFont = true, ApplyFill = true },
                TableFormat(0U, 0U), TableFormat(0U, 0U, 164U), TableFormat(0U, 0U)));
    }

    private static (int Width, int Height) GetImageDimensions(byte[] bytes, string mime)
    {
        if (mime == "image/png" && bytes.Length >= 24)
        {
            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            if (width is > 0 and < 100_000 && height is > 0 and < 100_000) return (width, height);
        }
        if (mime == "image/jpeg")
        {
            var index = 2;
            while (index + 9 < bytes.Length)
            {
                if (bytes[index] != 0xff) { index++; continue; }
                var marker = bytes[index + 1];
                if (marker is 0xd8 or 0xd9 or 0x01 || marker is >= 0xd0 and <= 0xd7)
                { index += 2; continue; }
                var length = (bytes[index + 2] << 8) | bytes[index + 3];
                if (length < 2 || index + 2 + length > bytes.Length) break;
                if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3)
                {
                    var height = (bytes[index + 5] << 8) | bytes[index + 6];
                    var width = (bytes[index + 7] << 8) | bytes[index + 8];
                    if (width > 0 && height > 0) return (width, height);
                }
                index += 2 + length;
            }
        }
        return (4, 3);
    }

    private static string Normalize(string text)
    {
        var value = text.Replace('\u00a0', ' ').Trim();
        if (value.Length > 32767)
        {
            var length = 32767;
            if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
            value = value[..length];
        }
        return NormalizeXmlText(value);
    }

    private static string NormalizeXmlText(string value)
    {
        // Excel XML does not allow raw XML 1.0 control chars or broken UTF-16 pairs.
        // Preserve valid tab/newline/CR and valid supplementary Unicode characters.
        var cleaned = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                { cleaned.Append(ch).Append(value[++i]); }
                else cleaned.Append('\uFFFD');
            }
            else if (char.IsLowSurrogate(ch)) cleaned.Append('\uFFFD');
            else if (ch is '\t' or '\n' or '\r' || ch >= 0x20 && ch is not '\uFFFE' and not '\uFFFF')
                cleaned.Append(ch);
        }
        return cleaned.ToString();
    }

    private static string ColumnName(int index)
    {
        var col = index + 1;
        var letters = "";
        do { col--; letters = (char)('A' + col % 26) + letters; col /= 26; } while (col > 0);
        return letters;
    }
}
