using Azunt.Editor.Export.Html;
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

    public static byte[] Export(SafeEditorDocument document)
    {
        using var buffer = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = package.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook();
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

            uint rowNumber = 0;
            uint drawingId = 1;
            long totalImageBytes = 0;
            DrawingsPart? drawingsPart = null;

            foreach (var node in document.Root.Children) WriteBlock(node);
            if (rowNumber == 0) AppendRow(["Document"]);

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
                    foreach (var tr in node.DescendantsAndSelf().Where(n => n.Name == "tr"))
                    {
                        var cells = tr.Children.Where(n => n.Name is "td" or "th")
                            .Take(100).ToList();
                        if (cells.Count == 0) continue;
                        AppendRow(cells.Select(c => Normalize(c.PlainText())).ToArray());
                        var rowImages = cells.SelectMany((cell, index) =>
                                cell.DescendantsAndSelf().Where(n => n.Name == "img")
                                    .Select(image => (Image: image, Column: index)))
                            .ToList();
                        var maxHeight = 0;
                        foreach (var (image, column) in rowImages)
                            maxHeight = Math.Max(maxHeight, AddImage(image, rowNumber - 1, column));
                        ReserveImageRows(maxHeight);
                    }
                    AppendRow([""]);
                    return;
                }

                if (node.Name is "h1" or "h2" or "h3" or "h4" or "p" or "li" or "blockquote" or "pre")
                {
                    WriteFlow(node);
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

            int AddImage(SafeEditorDocument.Node node, uint zeroBasedRow, int column)
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
                var width = 480;
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
                        RowOffset = new Xdr.RowOffset("0")
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

            void AppendRow(string[] cells)
            {
                if (rowNumber >= MaxRows) throw new InvalidOperationException("Excel row limit for this export was exceeded.");
                rowNumber++;
                var row = new S.Row { RowIndex = rowNumber };
                for (int col = 0; col < cells.Length; col++)
                {
                    // Inline strings also protect leading '=' and '+' from formula execution.
                    row.Append(new S.Cell
                    {
                        CellReference = ColumnName(col) + rowNumber.ToString(CultureInfo.InvariantCulture),
                        DataType = S.CellValues.InlineString,
                        InlineString = new S.InlineString(new S.Text(cells[col])
                        { Space = SpaceProcessingModeValues.Preserve })
                    });
                }
                sheetData.Append(row);
            }
        }
        return buffer.ToArray();
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
        return value.Length <= 32767 ? value : value[..32767];
    }

    private static string ColumnName(int index)
    {
        var col = index + 1;
        var letters = "";
        do { col--; letters = (char)('A' + col % 26) + letters; col /= 26; } while (col > 0);
        return letters;
    }
}
