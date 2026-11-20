using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Azunt.Editor.Export.Html;

/// <summary>
/// Conservative, purpose-built reader for the limited Azunt.Editor HTML subset.
/// Does NOT constitute a general-purpose sanitizer for arbitrary web HTML.
/// Outside content must be validated against the application's trust policy before storage.
/// </summary>
internal sealed class SafeEditorDocument
{
    internal sealed class Node(string name = "#root", string text = "")
    {
        public string Name { get; } = name;
        public string Text { get; } = text;
        public List<Node> Children { get; } = [];
        public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Attribute(string key) => Attributes.TryGetValue(key, out var value) ? value : "";
        public string Style(string key)
        {
            var style = Attribute("style");
            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = declaration.IndexOf(':');
                if (colon > 0 && declaration[..colon].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return declaration[(colon + 1)..].Trim();
            }
            return "";
        }
        public IEnumerable<Node> DescendantsAndSelf()
        {
            yield return this;
            foreach (var child in Children)
                foreach (var descendant in child.DescendantsAndSelf()) yield return descendant;
        }
        public string PlainText() => Name == "#text" ? Text :
            Name == "input" ? FormControlGlyph(this) : string.Concat(Children.Select(c => c.PlainText()));
    }

    internal static string FormControlGlyph(Node node)
        => node.Attribute("type").Trim().ToLowerInvariant() switch
        {
            "checkbox" => node.Attributes.ContainsKey("checked") ? "☑" : "☐",
            "radio" => node.Attributes.ContainsKey("checked") ? "◉" : "○",
            _ => ""
        };

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "del", "strike",
        "ul", "ol", "li", "blockquote", "pre", "code", "sup", "sub", "table", "thead", "tbody",
        "tfoot", "tr", "td", "th", "br", "hr", "img", "a", "span", "input"
    };
    private static readonly HashSet<string> Omitted = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "frame", "frameset", "object", "embed", "svg", "math", "template",
        "form", "textarea", "button", "select", "option", "link", "meta", "base", "video", "audio"
    };
    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase) { "br", "hr", "img", "input" };
    // Standard HTML void elements have no end tag. They must never cause the
    // remainder of an otherwise valid document to be discarded.
    private static readonly HashSet<string> OmittedVoid = new(StringComparer.OrdinalIgnoreCase)
    { "link", "meta", "base" };
    private const int MaxElementDepth = 128;
    private static readonly HashSet<string> Fonts = new(StringComparer.OrdinalIgnoreCase)
    { "Arial", "Georgia", "Times New Roman", "Verdana", "Segoe UI", "Noto Sans KR", "Malgun Gothic" };
    private static readonly Regex AttrPattern = new(@"(?<key>[A-Za-z][A-Za-z0-9_-]*)(?:\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+)))?", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex HexColor = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex Pixel = new(@"^(?<value>\d{1,4})px$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Node Root { get; } = new();

    public static SafeEditorDocument Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (html.Length > 12_000_000)
            throw new ArgumentException("HTML exceeds the 12-million-character export limit.", nameof(html));

        var doc = new SafeEditorDocument();
        var stack = new Stack<Node>();
        stack.Push(doc.Root);
        var skip = new Stack<string>();
        var count = 0;
        var offset = 0;

        while (offset < html.Length)
        {
            if (++count > 120_000) throw new ArgumentException("HTML is too complex to export.", nameof(html));
            var begin = html.IndexOf('<', offset);
            if (begin < 0)
            {
                AddText(html[offset..]);
                break;
            }
            if (begin > offset) AddText(html[offset..begin]);
            if (html.AsSpan(begin).StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = html.IndexOf("-->", begin + 4, StringComparison.Ordinal);
                offset = commentEnd < 0 ? html.Length : commentEnd + 3;
                continue;
            }
            var end = FindTagEnd(html, begin + 1);
            if (end < 0) { AddText("<"); offset = begin + 1; continue; }
            var token = html[(begin + 1)..end].Trim();
            offset = end + 1;
            if (token.Length == 0 || token[0] is '!' or '?') continue;
            var closing = token[0] == '/';
            if (closing) token = token[1..].TrimStart();
            var nameEnd = 0;
            while (nameEnd < token.Length && (char.IsLetterOrDigit(token[nameEnd]) || token[nameEnd] == '-')) nameEnd++;
            if (nameEnd == 0) continue;
            var tag = token[..nameEnd].ToLowerInvariant();
            if (skip.Count > 0)
            {
                if (Omitted.Contains(tag) && !closing && !OmittedVoid.Contains(tag) && !token.EndsWith('/'))
                    skip.Push(tag);
                else if (closing && tag == skip.Peek()) skip.Pop();
                continue;
            }
            if (Omitted.Contains(tag))
            {
                if (!closing && !OmittedVoid.Contains(tag) && !token.EndsWith('/'))
                    skip.Push(tag);
                continue;
            }
            if (!Allowed.Contains(tag)) continue;
            if (closing)
            {
                // Pop only a matching open element to recover from malformed editor HTML.
                if (stack.Any(n => n.Name == tag))
                    while (stack.Count > 1 && stack.Pop().Name != tag) { }
                continue;
            }
            var node = new Node(tag);
            foreach (Match match in AttrPattern.Matches(token[nameEnd..]))
            {
                var key = match.Groups["key"].Value;
                if (key is not ("src" or "href" or "alt" or "width" or "height" or "style" or "data-azunt-page-break" or "colspan" or "type" or "checked")) continue;
                var value = match.Groups["double"].Success ? match.Groups["double"].Value
                    : match.Groups["single"].Success ? match.Groups["single"].Value
                    : match.Groups["bare"].Value;
                node.Attributes[key] = WebUtility.HtmlDecode(value);
            }
            stack.Peek().Children.Add(node);
            if (!Void.Contains(tag) && !token.EndsWith('/'))
            {
                if (stack.Count >= MaxElementDepth)
                    throw new ArgumentException("HTML nesting exceeds the supported export limit.", nameof(html));
                stack.Push(node);
            }
        }
        return doc;

        void AddText(string raw)
        {
            if (skip.Count > 0 || raw.Length == 0) return;
            stack.Peek().Children.Add(new Node("#text", WebUtility.HtmlDecode(raw)));
        }
    }

    private static int FindTagEnd(string html, int start)
    {
        char quote = '\0';
        for (int i = start; i < html.Length; i++)
        {
            var current = html[i];
            if (quote == '\0')
            {
                if (current is '\'' or '"') quote = current;
                else if (current == '>') return i;
            }
            else if (current == quote) quote = '\0';
        }
        return -1;
    }

    public string ToSafeHtml(Func<string, bool>? isManagedImageUrl = null)
    {
        var sb = new StringBuilder();
        foreach (var child in Root.Children) Append(child, sb, isManagedImageUrl);
        return sb.ToString();
    }

    private static void Append(Node node, StringBuilder output, Func<string, bool>? isManagedImageUrl)
    {
        if (node.Name == "#text") { output.Append(HtmlEncoder.Default.Encode(node.Text)); return; }
        var inputType = node.Attribute("type").Trim().ToLowerInvariant();
        if (node.Name == "input" && inputType is not ("checkbox" or "radio")) return;
        if (node.Name == "img")
        {
            var src = node.Attribute("src");
            if (!TryInlineImage(node, out _, out _) &&
                !(isManagedImageUrl is not null && IsSafeLocalImageReference(src) && isManagedImageUrl(src)))
                return; // Never allow arbitrary remote image URLs.
        }
        output.Append('<').Append(node.Name);
        if (node.Name == "input")
        {
            output.Append(" type=\"").Append(inputType).Append('"');
            if (node.Attributes.ContainsKey("checked")) output.Append(" checked");
        }
        if (node.Name == "img")
        {
            output.Append(" src=\"").Append(HtmlEncoder.Default.Encode(node.Attribute("src"))).Append('"');
            output.Append(" alt=\"").Append(HtmlEncoder.Default.Encode(node.Attribute("alt")[..Math.Min(200, node.Attribute("alt").Length)])).Append('"');
            foreach (var dimension in new[] { "width", "height" })
                if (int.TryParse(node.Attribute(dimension), out var size) && size is >= 1 and <= 2500)
                    output.Append(' ').Append(dimension).Append("=\"").Append(size).Append('"');
        }
        if ((node.Name is "td" or "th") && int.TryParse(node.Attribute("colspan"), out var span) && span is >= 2 and <= 12)
            output.Append(" colspan=\"").Append(span).Append('"');
        if (node.Name == "hr" && node.Attribute("data-azunt-page-break") == "true")
            output.Append(" data-azunt-page-break=\"true\"");
        if (node.Name == "a" && TryLink(node.Attribute("href"), out var href))
            output.Append(" href=\"").Append(HtmlEncoder.Default.Encode(href)).Append("\" rel=\"noopener noreferrer\"");
        var style = SafeStyle(node);
        if (style.Length > 0) output.Append(" style=\"").Append(HtmlEncoder.Default.Encode(style)).Append('"');
        output.Append('>');
        if (Void.Contains(node.Name)) return;
        foreach (var child in node.Children) Append(child, output, isManagedImageUrl);
        output.Append("</").Append(node.Name).Append('>');
    }

    internal static bool IsSafeLocalImageReference(string? url)
        => !string.IsNullOrWhiteSpace(url) && url[0] == '/' && !url.StartsWith("//") &&
           url.Length <= 512 && !url.Contains('\\') && !url.Contains('%') &&
           !url.Contains('?') && !url.Contains('#') && !url.Contains("..", StringComparison.Ordinal) &&
           !url.Any(char.IsControl);

    /// <summary>
    /// Accept safe inline raster formats for browser rendering and persistence.
    /// Never retrieve remote image URLs. The Open XML writers currently embed
    /// PNG/JPEG only; GIF/WebP remain available for HTML/PDF.
    /// </summary>
    public static bool TryInlineImage(Node node, out string mime, out byte[] bytes)
    {
        mime = ""; bytes = [];
        var src = node.Attribute("src");
        var comma = src.IndexOf(',');
        if (comma <= 0 || comma >= src.Length - 1) return false;
        var prefix = src[..comma].ToLowerInvariant();
        mime = prefix switch
        {
            "data:image/png;base64" => "image/png",
            "data:image/jpeg;base64" => "image/jpeg",
            "data:image/gif;base64" => "image/gif",
            "data:image/webp;base64" => "image/webp",
            _ => ""
        };
        if (mime.Length == 0 || src.Length - comma - 1 > 4 * 1024 * 1024) return false;
        try
        {
            bytes = Convert.FromBase64String(src[(comma + 1)..]);
            if (bytes.Length == 0 || bytes.Length > 3 * 1024 * 1024) return false;
            return mime switch
            {
                "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "image/jpeg" => bytes.Length > 3 && bytes[0] == 0xff && bytes[1] == 0xd8,
                "image/gif" => bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8),
                "image/webp" => bytes.Length >= 12 && bytes.AsSpan().StartsWith("RIFF"u8) &&
                    bytes.AsSpan(8).StartsWith("WEBP"u8),
                _ => false
            };
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Open XML writers can embed supported PNG and JPEG images.</summary>
    public static bool TryImage(Node node, out string mime, out byte[] bytes)
    {
        return TryInlineImage(node, out mime, out bytes) && (mime is "image/png" or "image/jpeg");
    }

    public static bool TryLink(string raw, out string safe)
    {
        safe = "";
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 2048 || raw.Any(char.IsControl)) return false;
        var candidate = raw.Trim();
        if (candidate.Contains('\\') || candidate.StartsWith("//", StringComparison.Ordinal)) return false;
        // Preserve the same ordinary link forms supported by the browser editor.
        if (Regex.IsMatch(candidate, @"^#[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant))
        {
            safe = candidate;
            return true;
        }
        if (candidate.StartsWith("/", StringComparison.Ordinal) &&
            Regex.IsMatch(candidate, @"^/[A-Za-z0-9/_~!$&'()*+,;=.?%#-]*$", RegexOptions.CultureInvariant))
        {
            safe = candidate;
            return true;
        }
        if (Regex.IsMatch(candidate, @"^[A-Za-z0-9][A-Za-z0-9/_~!$&'()*+,;=.?%#-]*$", RegexOptions.CultureInvariant))
        {
            safe = candidate;
            return true;
        }
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (uri.Scheme is not ("https" or "http" or "mailto")) return false;
        safe = uri.AbsoluteUri;
        return true;
    }

    private static string SafeStyle(Node node)
    {
        var styles = new List<string>();
        var family = node.Style("font-family").Trim(' ', '\'', '"');
        if (Fonts.Contains(family)) styles.Add($"font-family:'{family}'");
        var fontSize = Pixel.Match(node.Style("font-size"));
        if (fontSize.Success && int.TryParse(fontSize.Groups["value"].Value, out var pixels) && pixels is >= 8 and <= 72)
            styles.Add($"font-size:{pixels}px");
        foreach (var colorStyle in new[] { "color", "background-color" })
        {
            var color = node.Style(colorStyle);
            if (HexColor.IsMatch(color)) styles.Add($"{colorStyle}:{color}");
        }
        var align = node.Style("text-align").ToLowerInvariant();
        if (align is "left" or "right" or "center" or "justify") styles.Add($"text-align:{align}");
        if (node.Name is "table" or "td" or "th")
        {
            var borderColor = node.Style("border-color");
            if (HexColor.IsMatch(borderColor)) styles.Add($"border-color:{borderColor}");
            var width = node.Style("border-width");
            if (width is "1px" or "2px" or "3px" or "4px") styles.Add($"border-width:{width}");
            if (styles.Any(s => s.StartsWith("border-", StringComparison.Ordinal))) styles.Add("border-style:solid");
            if (node.Name == "table" && node.Style("table-layout") == "fixed")
                styles.Add("table-layout:fixed");
            if (node.Name is "td" or "th")
            {
                var columnWidth = node.Style("width");
                if (Regex.IsMatch(columnWidth, @"^(?:100|[1-9]?\d(?:\.\d{1,3})?)%$",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                    styles.Add($"width:{columnWidth}");
            }
        }
        var indent = Pixel.Match(node.Style("margin-left"));
        if (indent.Success && int.TryParse(indent.Groups["value"].Value, out var px) && px <= 240)
            styles.Add($"margin-left:{px}px");
        return string.Join(';', styles);
    }
}
