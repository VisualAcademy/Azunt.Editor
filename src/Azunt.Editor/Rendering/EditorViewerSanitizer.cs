using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Azunt.Editor.Rendering;

/// <summary>
/// Dependency-free parser for the narrow editor HTML subset used by server-rendered View mode.
/// The host still owns storage validation and access control.
/// </summary>
internal sealed class ViewerSafeEditorDocument
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
        public string PlainText() => Name == "#text" ? Text : string.Concat(Children.Select(c => c.PlainText()));
    }

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "del", "strike",
        "ul", "ol", "li", "blockquote", "pre", "code", "sup", "sub", "table", "thead", "tbody",
        "tfoot", "tr", "td", "th", "br", "hr", "img", "a", "span", "input", "font"
    };
    private static readonly HashSet<string> Omitted = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "frame", "frameset", "object", "embed", "svg", "math", "template",
        "form", "textarea", "button", "select", "option", "link", "meta", "base", "video", "audio",
        "noscript", "noembed", "noframes", "plaintext", "xmp", "title", "head"
    };
    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase) { "br", "hr", "img", "input" };
    // Standard HTML void elements have no end tag. They must never cause the
    // remainder of an otherwise valid document to be discarded.
    private static readonly HashSet<string> OmittedVoid = new(StringComparer.OrdinalIgnoreCase)
    { "link", "meta", "base" };
    private const int MaxElementDepth = 128;
    private static readonly HashSet<string> Fonts = new(StringComparer.OrdinalIgnoreCase)
    { "Arial", "Georgia", "Times New Roman", "Verdana", "Segoe UI", "Segoe Script", "Lucida Handwriting", "Brush Script MT", "Apple Chancery", "Noto Sans KR", "Malgun Gothic" };
    private static readonly Regex AttrPattern = new(@"(?<key>[A-Za-z][A-Za-z0-9_-]*)(?:\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+)))?", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex HexColor = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex Pixel = new(@"^(?<value>\d{1,4})px$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Node Root { get; } = new();

    public static ViewerSafeEditorDocument Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (html.Length > 12_000_000)
            throw new ArgumentException("HTML exceeds the 12-million-character viewer limit.", nameof(html));

        var doc = new ViewerSafeEditorDocument();
        var stack = new Stack<Node>();
        stack.Push(doc.Root);
        var skip = new Stack<string>();
        var count = 0;
        var offset = 0;

        while (offset < html.Length)
        {
            if (++count > 120_000) throw new ArgumentException("HTML is too complex to render.", nameof(html));
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
                var key = match.Groups["key"].Value.ToLowerInvariant();
                if (key is not ("src" or "href" or "alt" or "width" or "height" or "style" or "data-azunt-page-break" or "data-language" or "colspan" or "type" or "checked" or "start" or "align" or "valign" or "face" or "size" or "color" or "border" or "class")) continue;
                var value = match.Groups["double"].Success ? match.Groups["double"].Value
                    : match.Groups["single"].Success ? match.Groups["single"].Value
                    : match.Groups["bare"].Value;
                node.Attributes[key] = WebUtility.HtmlDecode(value);
            }
            NormalizeLegacy(node);
            stack.Peek().Children.Add(node);
            if (!Void.Contains(tag) && !token.EndsWith('/'))
            {
                if (stack.Count >= MaxElementDepth)
                    throw new ArgumentException("HTML nesting exceeds the supported viewer limit.", nameof(html));
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

    public string ToSafeHtml()
    {
        var sb = new StringBuilder();
        foreach (var child in Root.Children) Append(child, sb);
        return sb.ToString();
    }

    private static bool IsCodeBlock(Node node)
        => node.Name == "pre" && (node.Attribute("data-language") is "csharp" or "javascript" or "sql" or "html" or "css")
           && node.Children.Any(child => child.Name == "code" && child.PlainText().Length <= 100_000);

    private static void Append(Node node, StringBuilder output)
    {
        if (node.Name == "#text") { output.Append(HtmlEncoder.Default.Encode(node.Text)); return; }
        var inputType = node.Attribute("type").Trim().ToLowerInvariant();
        if (node.Name == "input" && inputType is not ("radio" or "checkbox")) return;
        if (node.Name == "img")
        {
            var src = node.Attribute("src");
            if (!TrySafeImageUrl(src))
                return; // Reject unsupported sources and unsafe URL schemes.
        }
        output.Append('<').Append(node.Name == "font" ? "span" : node.Name);
        if (node.Name == "pre" && IsCodeBlock(node))
            output.Append(" class=\"az-code-block\" data-language=\"").Append(node.Attribute("data-language")).Append('"');
        if (node.Name == "input")
        {
            output.Append(" type=\"").Append(inputType).Append('"');
            if (node.Attributes.ContainsKey("checked")) output.Append(" checked");
            // Server-rendered View is non-interactive even without JavaScript.
            output.Append(" disabled");
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
        if (node.Name == "ol" && Regex.IsMatch(node.Attribute("start"), @"\A[1-9]\d{0,3}\z"))
            output.Append(" start=\"").Append(node.Attribute("start")).Append('"');
        var style = SafeStyle(node);
        if (style.Length > 0) output.Append(" style=\"").Append(HtmlEncoder.Default.Encode(style)).Append('"');
        output.Append('>');
        if (Void.Contains(node.Name)) return;
        if (node.Name == "pre" && IsCodeBlock(node))
        {
            var source = node.Children.First(child => child.Name == "code");
            output.Append("<code>").Append(HtmlEncoder.Default.Encode(source.PlainText())).Append("</code>");
        }
        else foreach (var child in node.Children) Append(child, output);
        output.Append("</").Append(node.Name == "font" ? "span" : node.Name).Append('>');
    }

    private static bool TrySafeImageUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 8 * 1024 * 1024 ||
            raw.Any(char.IsControl)) return false;
        var src = raw.Trim();
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var image = new Node("img");
            image.Attributes["src"] = src;
            return TryInlineImage(image, out _, out _);
        }
        if (src.Contains('\\') || src.StartsWith("//", StringComparison.Ordinal)) return false;
        if (src.StartsWith("/", StringComparison.Ordinal))
            return Regex.IsMatch(src, @"^/[A-Za-z0-9/_~!$&'()*+,;=.?%#-]*$", RegexOptions.CultureInvariant);
        if (Regex.IsMatch(src, @"^[A-Za-z0-9][A-Za-z0-9/_~!$&'()*+,;=.?%#-]*$", RegexOptions.CultureInvariant))
            return true;
        if (!Uri.TryCreate(src, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0) return false;
        return uri.Scheme is "http" or "https";
    }

    /// <summary>
    /// Accept safe inline raster image formats. Only data bytes are validated;
    /// the server-side viewer never performs remote image requests.
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
        if (mime.Length == 0 || src.Length - comma - 1 > 8 * 1024 * 1024) return false;
        try
        {
            bytes = Convert.FromBase64String(src[(comma + 1)..]);
            if (bytes.Length == 0 || bytes.Length > 6 * 1024 * 1024) return false;
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

    // The editing engine serializes browser-selected colors as rgb(...) rather
    // than hex. Preserve these when SSR sanitizes View, instead of losing saved
    // formatting on a subsequent View -> Edit transition.
    private static bool TryColor(string source, out string color)
    {
        color = "";
        var raw = source.Trim();
        if (HexColor.IsMatch(raw)) { color = raw.ToLowerInvariant(); return true; }
        var match = Regex.Match(raw,
            @"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*1(?:\.0+)?)?\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return false;
        var rgb = Enumerable.Range(1, 3).Select(i => int.Parse(match.Groups[i].Value)).ToArray();
        if (rgb.Any(n => n > 255)) return false;
        color = $"#{rgb[0]:x2}{rgb[1]:x2}{rgb[2]:x2}";
        return true;
    }

    // Only recognize specific legacy formatting. Never copy CSS declarations wholesale to output.
    private static void NormalizeLegacy(Node node)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in node.Attribute("style").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon > 0) properties.TryAdd(declaration[..colon].Trim(), declaration[(colon + 1)..].Trim());
        }
        string Read(string key) => properties.TryGetValue(key, out var value) ? value : "";
        void Put(string key, string value) { if (value.Length > 0) properties[key] = value; }
        var family = Read("font-family");
        if (family.Length == 0 && node.Name == "font") family = node.Attribute("face");
        family = family.Split(',')[0].Trim(' ', '\'', '"');
        var knownFamily = Fonts.FirstOrDefault(f => f.Equals(family, StringComparison.OrdinalIgnoreCase));
        if (knownFamily is not null) Put("font-family", knownFamily);
        else if (family.Equals("Cedarville Cursive", StringComparison.OrdinalIgnoreCase)) Put("font-family", "Cedarville Cursive");
        var size = Read("font-size").ToLowerInvariant();
        var pixelSize = size switch { "xx-small" => 9, "x-small" => 10, "small" => 13, "medium" => 16, "large" => 18, "x-large" => 24, "xx-large" => 32, _ => 0 };
        if (pixelSize == 0 && size.Length == 0 && node.Name == "font" && int.TryParse(node.Attribute("size"), out var legacySize) && legacySize is >= 1 and <= 7)
            pixelSize = new[] { 10, 13, 16, 18, 24, 32, 48 }[legacySize - 1];
        if (pixelSize > 0) Put("font-size", $"{pixelSize}px");
        if (node.Name == "font" && Read("color").Length == 0) Put("color", node.Attribute("color"));
        foreach (var key in new[] { "color", "background-color", "border-color", "border-top-color" })
            if (LegacyColor(Read(key)) is string color) Put(key, color);
        if (Read("text-align").Length == 0)
        {
            var align = node.Attribute("align").ToLowerInvariant();
            if (align.Length == 0)
                align = new[] { "center", "right", "left" }.FirstOrDefault(a => node.Attribute("class").Split(' ').Contains("text-" + a)) ?? "";
            if (align is "left" or "right" or "center" or "justify") Put("text-align", align);
        }
        if ((node.Name is "ol" or "ul") && Read("list-style-type").Length == 0)
            Put("list-style-type", node.Attribute("type") switch { "1" => "decimal", "a" => "lower-alpha", "A" => "upper-alpha", "i" => "lower-roman", "I" => "upper-roman", "disc" => "disc", "circle" => "circle", "square" => "square", _ => "" });
        if (node.Name is "table" or "td" or "th")
        {
            var width = Read("width");
            if (width.Length == 0) width = node.Attribute("width");
            if (Regex.IsMatch(width, @"\A(?:[1-9]\d{0,2}|1\d{3}|2000)\z")) width += "px";
            if (Regex.IsMatch(width, @"\A\d{1,3}\.\d{4,12}%\z") && double.TryParse(width[..^1], System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var percent) && percent <= 100)
                width = Math.Round(percent, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";
            Put("width", width);
            if (Read("border") is ("0" or "0px" or "none") || Read("border-style") == "none" || Read("border-width") is ("0" or "0px") || (node.Name == "table" && node.Attribute("border") == "0"))
                Put("border-style", "none");
            // Chromium serializes a top-only border over a zero border using a three-value width shorthand.
            var topOnly = Regex.Match(Read("border-width"), @"\A([1-4]px) 0px 0px(?: 0px)?\z");
            if (topOnly.Success && Read("border-top-style") == "solid")
            {
                Put("border-style", "none");
                Put("border-top-width", topOnly.Groups[1].Value);
            }
            var top = Regex.Match(Read("border-top"), @"\A([1-4]px)\s+solid\s+(.+)\z");
            if (top.Success && LegacyColor(top.Groups[2].Value) is string topColor)
            { Put("border-top-width", top.Groups[1].Value); Put("border-top-style", "solid"); Put("border-top-color", topColor); }
            if (Read("vertical-align").Length == 0) Put("vertical-align", node.Attribute("valign"));
            if (Read("white-space").Length == 0 && node.Attribute("class").Split(' ').Contains("text-nowrap")) Put("white-space", "nowrap");
        }
        if (node.Name == "img")
            foreach (var key in new[] { "width", "height" })
                if (Pixel.Match(Read(key)) is { Success: true } dimension && int.TryParse(dimension.Groups["value"].Value, out var n) && n is >= 1 and <= 2500)
                    node.Attributes[key] = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Read("text-decoration-line").Length == 0 && Read("text-decoration") is ("underline" or "line-through" or "underline line-through" or "line-through underline" or "none")) Put("text-decoration-line", Read("text-decoration"));
        node.Attributes["style"] = string.Join(';', properties.Select(p => $"{p.Key}:{p.Value}"));
    }

    private static string? LegacyColor(string source)
    {
        var raw = source.Trim().ToLowerInvariant();
        if (HexColor.IsMatch(raw)) return raw;
        if (Regex.IsMatch(raw, @"\A#[0-9a-f]{3}\z")) return "#" + string.Concat(raw[1..].Select(c => new string(c, 2)));
        var named = raw switch { "black" => "#000000", "white" => "#ffffff", "red" => "#ff0000", "blue" => "#0000ff", "green" => "#008000", "yellow" => "#ffff00", "skyblue" => "#87ceeb", "gray" or "grey" => "#808080", _ => null };
        if (named is not null) return named;
        var rgb = Regex.Match(raw, @"\Argb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)\z");
        if (!rgb.Success) return null;
        var parts = Enumerable.Range(1, 3).Select(i => int.Parse(rgb.Groups[i].Value)).ToArray();
        return parts.Any(n => n > 255) ? null : $"#{parts[0]:x2}{parts[1]:x2}{parts[2]:x2}";
    }

    private static string SafeStyle(Node node)
    {
        var styles = new List<string>();
        var family = node.Style("font-family").Trim(' ', '\'', '"');
        if (Fonts.Contains(family) || family == "Cedarville Cursive") styles.Add($"font-family:'{family}'");
        var fontSize = Pixel.Match(node.Style("font-size"));
        if (fontSize.Success && int.TryParse(fontSize.Groups["value"].Value, out var pixels) && pixels is >= 8 and <= 72)
            styles.Add($"font-size:{pixels}px");
        if (Regex.IsMatch(node.Style("font-size"), @"\A(?:[6-9]|[1-4]\d|5[0-4])pt\z")) styles.Add($"font-size:{node.Style("font-size")}");
        if (Regex.IsMatch(node.Style("font-weight"), @"\A(?:normal|bold|[1-9]00)\z")) styles.Add($"font-weight:{node.Style("font-weight")}");
        if (node.Style("font-style") is "normal" or "italic") styles.Add($"font-style:{node.Style("font-style")}");
        if (node.Style("text-decoration-line") is "none" or "underline" or "line-through" or "underline line-through" or "line-through underline") styles.Add($"text-decoration-line:{node.Style("text-decoration-line")}");
        if ((node.Name is "ol" or "ul") && node.Style("list-style-type") is ("decimal" or "lower-alpha" or "upper-alpha" or "lower-roman" or "upper-roman" or "disc" or "circle" or "square")) styles.Add($"list-style-type:{node.Style("list-style-type")}");
        foreach (var colorStyle in new[] { "color", "background-color" })
        {
            var color = node.Style(colorStyle);
            if (TryColor(color, out var normalized)) styles.Add($"{colorStyle}:{normalized}");
        }
        var align = node.Style("text-align").ToLowerInvariant();
        if (align is "left" or "right" or "center" or "justify") styles.Add($"text-align:{align}");
        if (node.Name == "tr" && Regex.IsMatch(node.Style("height"), @"^(?:[1-9]\d{0,2}|1000)px$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            styles.Add($"height:{node.Style("height")}");
        if (node.Name is "table" or "td" or "th")
        {
            var borderColor = node.Style("border-color");
            if (TryColor(borderColor, out var normalizedBorder)) styles.Add($"border-color:{normalizedBorder}");
            var width = node.Style("border-width");
            if (width is "1px" or "2px" or "3px" or "4px") styles.Add($"border-width:{width}");
            if (node.Style("border-style") == "none") styles.Add("border:0");
            else if (styles.Any(s => s.StartsWith("border-", StringComparison.Ordinal))) styles.Add("border-style:solid");
            if (node.Name != "table" && Regex.IsMatch(node.Style("width"), @"\A(?:[1-9]\d{0,2}|1\d{3}|2000)px\z")) styles.Add($"width:{node.Style("width")}");
            if (node.Name is "td" or "th")
            {
                if (node.Style("vertical-align") is "top" or "middle" or "bottom") styles.Add($"vertical-align:{node.Style("vertical-align")}");
                if (node.Style("white-space") == "nowrap") styles.Add("white-space:nowrap");
                if (node.Style("border-top-style") == "solid" && node.Style("border-top-width") is ("1px" or "2px" or "3px" or "4px") && HexColor.IsMatch(node.Style("border-top-color")))
                    { styles.Add($"border-top-width:{node.Style("border-top-width")}"); styles.Add("border-top-style:solid"); styles.Add($"border-top-color:{node.Style("border-top-color")}"); }
            }
            if (node.Name == "table" && Regex.IsMatch(node.Style("width"), @"^(?:100|[1-9]\d?(?:\.\d{1,3})?)%$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                styles.Add($"width:{node.Style("width")}");
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
        if (indent.Success && int.TryParse(indent.Groups["value"].Value, out var px) && px <= 240 && px % 24 == 0)
            styles.Add($"margin-left:{px}px");
        return string.Join(';', styles);
    }
}

/// <summary>
/// HTML allowlist for server-side rendering of Azunt.Editor View documents.
/// Does not fetch images; it is not a universal HTML sanitizer or a host authorization boundary.
/// </summary>
public static class EditorViewerSanitizer
{
    /// <summary>Returns safe markup in the editor's supported subset for server-rendered View mode.</summary>
    public static string Sanitize(string? html)
        => ViewerSafeEditorDocument.Parse(html ?? string.Empty).ToSafeHtml();
}
