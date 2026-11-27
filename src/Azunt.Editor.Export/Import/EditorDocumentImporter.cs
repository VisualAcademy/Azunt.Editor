using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Azunt.Editor.Export.Html;

namespace Azunt.Editor.Export.Import;

/// <summary>Imported editable HTML and notices about fidelity limitations.</summary>
public sealed record EditorImportResult(string Html, IReadOnlyList<string> Warnings);

/// <summary>
/// Opt-in, bounded conversion of UTF-8 HTML/text and basic DOCX documents into the
/// editor's restricted HTML subset. Does not execute macros or fetch resources.
/// The caller is responsible for upload authorization and explicit replacement.
/// </summary>
public static class EditorDocumentImporter
{
    public const int MaxFileBytes = 8 * 1024 * 1024;
    private const int MaxXmlBytes = 16 * 1024 * 1024;
    private const int MaxImageBytes = 2 * 1024 * 1024;
    private const int MaxTotalImageBytes = 6 * 1024 * 1024;
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static EditorImportResult Import(string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0 || content.Length > MaxFileBytes)
            throw new ArgumentException("Import files must be between 1 byte and 8 MiB.", nameof(content));
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        string html;
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".txt":
                var text = Decode(content);
                var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
                if (normalized.Count(c => c == '\n') > 30_000) throw new InvalidDataException("Text has too many lines.");
                var builder = new StringBuilder();
                foreach (var line in normalized.Split('\n'))
                {
                    builder.Append("<p>").Append(line.Length == 0 ? "<br>" : Encode(line)).Append("</p>");
                    if (builder.Length > EditorHtmlSanitizer.MaxHtmlCharacters) throw new InvalidDataException("Converted text is too large.");
                }
                html = builder.ToString();
                break;
            case ".html": case ".htm":
                html = Decode(content);
                // Full HTML documents normally contain a body. This extracts it for
                // presentation only; the restrictive sanitizer below remains mandatory.
                var body = Regex.Match(html, @"<body\b[^>]*>([\s\S]*?)</body\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (body.Success) html = body.Groups[1].Value;
                warnings.Add("HTML is restricted to supported editor markup. External images and unsupported styles are excluded.");
                break;
            case ".docx":
                using (var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read))
                    html = new WordReader(archive, warnings).Convert();
                warnings.Add("DOCX import converts body content, not the original Word layout. Review the result before replacing your document.");
                break;
            default: throw new NotSupportedException("Supported import formats: .html, .htm, .txt, .docx.");
        }
        if (html.Length > EditorHtmlSanitizer.MaxHtmlCharacters)
            throw new InvalidDataException("Converted document is too large.");
        html = EditorHtmlSanitizer.Sanitize(html);
        if (html.Length > EditorHtmlSanitizer.MaxHtmlCharacters || Encoding.UTF8.GetByteCount(html) > 16 * 1024 * 1024)
            throw new InvalidDataException("Sanitized document is too large.");
        if (string.IsNullOrWhiteSpace(html)) throw new InvalidDataException("The document has no supported content.");
        return new EditorImportResult(html, warnings.ToArray());
    }

    private static string Decode(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
    private static string Encode(string? text) => HtmlEncoder.Default.Encode(text ?? "");

    private sealed class WordReader
    {
        private readonly HashSet<string> _warnings;
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _images = new(StringComparer.Ordinal);
        private readonly StringBuilder _html = new();
        private int _totalImages, _imageCount;
        public WordReader(ZipArchive archive, HashSet<string> warnings)
        {
            _warnings = warnings;
            if (archive.Entries.Count > 2048) throw new InvalidDataException("DOCX contains too many parts.");
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.Length < 0 || entry.Length > 64L * 1024 * 1024 || (expanded += entry.Length) > 64L * 1024 * 1024)
                    throw new InvalidDataException("DOCX expanded size exceeds 64 MiB.");
                if (!_entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("Duplicate DOCX part.");
                if (entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Macro-enabled documents are not supported.");
            }
        }
        public string Convert()
        {
            var types = ReadXml("[Content_Types].xml", true)!;
            // Open XML SDK can emit a Default xml content type rather than an
            // Override for document.xml. Support both, with Override taking priority.
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            var typesRoot = types.Root;
            if (typesRoot?.Name != ct + "Types") throw new InvalidDataException("Invalid DOCX content types.");
            var declared = typesRoot.Elements(ct + "Override").FirstOrDefault(e => (string?)e.Attribute("PartName") == "/word/document.xml")
                ?? typesRoot.Elements(ct + "Default").FirstOrDefault(e => string.Equals((string?)e.Attribute("Extension"), "xml", StringComparison.OrdinalIgnoreCase));
            if ((string?)declared?.Attribute("ContentType") != "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")
                throw new InvalidDataException("Not a supported DOCX document package.");
            var relationships = ReadXml("word/_rels/document.xml.rels", false);
            if (relationships?.Root is not null)
                foreach (var rel in relationships.Root.Elements(Rel + "Relationship"))
                {
                    var id = (string?)rel.Attribute("Id"); var target = (string?)rel.Attribute("Target");
                    if (id is null || target is null || (string?)rel.Attribute("TargetMode") == "External" ||
                        (string?)rel.Attribute("Type") != R.NamespaceName + "/image") continue;
                    // Only ordinary package paths; no URI resolution or filesystem access.
                    if (target.Length > 512 || !Regex.IsMatch(target, @"^/?[A-Za-z0-9_./-]+$", RegexOptions.CultureInvariant) || target.Contains("//")) continue;
                    var parts = new List<string>();
                    if (!target.StartsWith('/')) parts.Add("word");
                    foreach (var part in target.TrimStart('/').Split('/'))
                    {
                        if (part == "." || part == "") continue;
                        if (part == "..") { if (parts.Count == 0) { parts.Clear(); break; } parts.RemoveAt(parts.Count - 1); }
                        else parts.Add(part);
                    }
                    var path = string.Join('/', parts);
                    if (path.Length > 0 && _entries.ContainsKey(path)) _images.TryAdd(id, path);
                }
            var doc = ReadXml("word/document.xml", true)!;
            if (doc.Root?.Name != W + "document") throw new InvalidDataException("Not a supported Word document XML.");
            var body = doc.Root.Element(W + "body") ?? throw new InvalidDataException("Missing Word document body.");
            if (body.Descendants(W + "numPr").Any()) _warnings.Add("Word list numbering is not reconstructed; paragraph text is retained.");
            if (body.Descendants(W + "vMerge").Any()) _warnings.Add("Vertical cell merges are flattened; cell content is retained.");
            if (body.Descendants(W + "hyperlink").Any()) _warnings.Add("Hyperlink text is retained without Word link targets.");
            if (body.Descendants(W + "sectPr").Any() || _entries.Keys.Any(k => k.StartsWith("word/header") || k.StartsWith("word/footer")))
                _warnings.Add("Word sections, headers, footers, and page settings are not imported.");
            if (body.Descendants().Any(e => e.Name == W + "sdt" || e.Name == W + "altChunk" || e.Name == W + "object" || e.Name == W + "pict" || e.Name == W + "ins" || e.Name == W + "del"))
                _warnings.Add("Unsupported Word objects, content controls and tracked changes may be omitted.");
            foreach (var child in body.Elements()) Block(child, 0);
            return _html.ToString();
        }
        private XDocument? ReadXml(string path, bool required)
        {
            if (!_entries.TryGetValue(path, out var entry))
            {
                if (required) throw new InvalidDataException($"Missing required DOCX part: {path}");
                return null;
            }
            if (entry.Length > MaxXmlBytes) throw new InvalidDataException("DOCX XML part exceeds the size limit.");
            // Preflight depth/node count before materializing an XML tree.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlBytes };
            using (var input = entry.Open()) using (var reader = XmlReader.Create(input, settings))
            {
                int count = 0;
                while (reader.Read()) if (reader.Depth > 64 || ++count > 300_000) throw new InvalidDataException("DOCX XML is too complex.");
            }
            using var stream = entry.Open(); using var xml = XmlReader.Create(stream, settings);
            return XDocument.Load(xml, LoadOptions.PreserveWhitespace);
        }
        private void Add(string value)
        {
            if (_html.Length + (long)value.Length > EditorHtmlSanitizer.MaxHtmlCharacters) throw new InvalidDataException("Converted DOCX is too large.");
            _html.Append(value);
        }
        private void Block(XElement element, int depth)
        {
            if (depth > 24) throw new InvalidDataException("DOCX tables are nested too deeply.");
            if (element.Name == W + "p") Paragraph(element);
            else if (element.Name == W + "tbl")
            {
                Add("<table><tbody>");
                foreach (var row in element.Elements(W + "tr"))
                {
                    Add("<tr>");
                    var tag = Enabled(row.Element(W + "trPr")?.Element(W + "tblHeader")) ? "th" : "td";
                    foreach (var cell in row.Elements(W + "tc"))
                    {
                        var span = IntVal(cell.Element(W + "tcPr")?.Element(W + "gridSpan"));
                        Add("<" + tag + (span is >= 2 and <= 12 ? $" colspan=\"{span}\"" : "") + ">");
                        if (span > 12) _warnings.Add("Cell spans above 12 are simplified.");
                        foreach (var item in cell.Elements()) Block(item, depth + 1);
                        Add("</" + tag + ">");
                    }
                    Add("</tr>");
                }
                Add("</tbody></table>");
            }
        }
        private static int IntVal(XElement? element) => int.TryParse((string?)element?.Attribute(W + "val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        private static bool Enabled(XElement? element) => element is not null && (string?)element.Attribute(W + "val") is not ("0" or "false" or "off" or "none");
        private void Paragraph(XElement paragraph)
        {
            var props = paragraph.Element(W + "pPr");
            var style = (string?)props?.Element(W + "pStyle")?.Attribute(W + "val");
            var tag = style?.ToLowerInvariant() switch { "heading1" => "h1", "heading2" => "h2", "heading3" => "h3", "heading4" => "h4", _ => "p" };
            var align = (string?)props?.Element(W + "jc")?.Attribute(W + "val");
            if (align == "both") align = "justify";
            var attr = align is "left" or "right" or "center" or "justify" ? $" style=\"text-align:{align}\"" : "";
            if (Enabled(props?.Element(W + "pageBreakBefore"))) Add("<hr data-azunt-page-break=\"true\">");
            Add("<" + tag + attr + ">");
            var hasContent = false;
            foreach (var child in paragraph.Elements())
            {
                if (child.Name == W + "r") { Run(child, tag, attr); hasContent = true; }
                else if (child.Name == W + "hyperlink") foreach (var run in child.Elements(W + "r")) { Run(run, tag, attr); hasContent = true; }
            }
            if (!hasContent) Add("<br>");
            Add("</" + tag + ">");
        }
        private void Run(XElement run, string paragraphTag, string paragraphAttr)
        {
            var props = run.Element(W + "rPr");
            var wrappers = new List<string>();
            if (Enabled(props?.Element(W + "b"))) wrappers.Add("strong");
            if (Enabled(props?.Element(W + "i"))) wrappers.Add("em");
            if (Enabled(props?.Element(W + "u"))) wrappers.Add("u");
            if (Enabled(props?.Element(W + "strike"))) wrappers.Add("s");
            var vertical = (string?)props?.Element(W + "vertAlign")?.Attribute(W + "val");
            if (vertical == "superscript") wrappers.Add("sup"); else if (vertical == "subscript") wrappers.Add("sub");
            var styles = new List<string>();
            var size = IntVal(props?.Element(W + "sz"));
            var px = (int)Math.Round(size * 2d / 3);
            if (px is >= 8 and <= 72) styles.Add($"font-size:{px}px");
            var color = (string?)props?.Element(W + "color")?.Attribute(W + "val");
            if (color is not null && Regex.IsMatch(color, "^[0-9a-fA-F]{6}$")) styles.Add("color:#" + color);
            void Open() { foreach (var wrapper in wrappers) Add("<" + wrapper + ">"); if (styles.Count > 0) Add("<span style=\"" + string.Join(';', styles) + "\">"); }
            void Close() { if (styles.Count > 0) Add("</span>"); foreach (var wrapper in wrappers.AsEnumerable().Reverse()) Add("</" + wrapper + ">"); }
            Open();
            foreach (var child in run.Elements())
            {
                if (child.Name == W + "t") Add(Encode(child.Value));
                else if (child.Name == W + "tab") Add("&nbsp;&nbsp;&nbsp;&nbsp;");
                else if (child.Name == W + "br" || child.Name == W + "cr")
                {
                    if ((string?)child.Attribute(W + "type") == "page")
                    {
                        Close(); Add("</" + paragraphTag + "><hr data-azunt-page-break=\"true\"><" + paragraphTag + paragraphAttr + ">"); Open();
                    }
                    else Add("<br>");
                }
                else if (child.Name == W + "drawing") Image(child);
            }
            Close();
        }
        private void Image(XElement drawing)
        {
            foreach (var blip in drawing.Descendants(A + "blip"))
            {
                var id = (string?)blip.Attribute(R + "embed");
                if (id is null || !_images.TryGetValue(id, out var path) || !_entries.TryGetValue(path, out var entry)) { _warnings.Add("External or unsupported images were omitted."); continue; }
                var mime = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", _ => null };
                if (mime is null || entry.Length <= 0 || entry.Length > MaxImageBytes || _imageCount >= 32 || _totalImages + entry.Length > MaxTotalImageBytes) { _warnings.Add("Some images exceeded the supported format or size limits."); continue; }
                using var source = entry.Open(); using var buffer = new MemoryStream();
                var bytes = new byte[8192]; int read;
                while ((read = source.Read(bytes, 0, bytes.Length)) > 0) { if (buffer.Length + read > MaxImageBytes) throw new InvalidDataException("Image exceeds size limit."); buffer.Write(bytes, 0, read); }
                _imageCount++; _totalImages += (int)buffer.Length;
                var dataUrl = $"data:{mime};base64,{System.Convert.ToBase64String(buffer.ToArray())}";
                var node = new SafeEditorDocument.Node("img");
                node.Attributes["src"] = dataUrl;
                if (!SafeEditorDocument.TryInlineImage(node, out _, out _)) { _warnings.Add("Invalid embedded raster image omitted."); continue; }
                var alt = (string?)drawing.Descendants(WP + "docPr").FirstOrDefault()?.Attribute("descr") ?? "";
                var extent = drawing.Descendants(WP + "extent").FirstOrDefault();
                var width = long.TryParse((string?)extent?.Attribute("cx"), out var emu) ? Math.Clamp(emu / 9525, 1, 2500) : 0;
                Add($"<img src=\"{dataUrl}\" alt=\"{Encode(alt)}\"{(width > 0 ? $" width=\"{width}\"" : "")}>");
            }
        }
    }
}
