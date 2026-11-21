using Azunt.Editor.Export.Formatting;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Azunt.Editor.Export.Pdf;

/// <summary>
/// Adds print-only lexical colors to sanitized code text. The matcher accepts
/// only the fixed markup emitted by SafeEditorDocument.ToSafeHtml(). It never
/// interprets or executes source code, and every token is HTML-encoded again.
/// </summary>
internal static class PdfCodeFormatter
{
    private static readonly Regex CodeBlock = new(
        @"(?<start><pre class=""az-code-block"" data-language=""(?<lang>csharp|javascript|sql|html|css)""(?: style=""[^""]*"")?><code>)(?<source>[^<]{0,150000})(?<end></code></pre>)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));

    internal static string HighlightSafeHtml(string html)
    {
        try { return CodeBlock.Replace(html, Highlight); }
        catch (RegexMatchTimeoutException) { return html; } // The sanitized original remains printable.
    }

    private static string Highlight(Match match)
    {
        var source = WebUtility.HtmlDecode(match.Groups["source"].Value);
        if (source.Length > 100_000) return match.Value;
        var output = new StringBuilder(match.Length + 128);
        output.Append(match.Groups["start"].Value);
        foreach (var token in CodeSyntaxTokens.Tokenize(source, match.Groups["lang"].Value))
        {
            var kind = token.Category;
            if (kind != CodeSyntaxTokens.Kind.Plain)
                output.Append("<span class=\"az-pdf-code-").Append(kind.ToString().ToLowerInvariant()).Append("\">");
            output.Append(HtmlEncoder.Default.Encode(token.Text));
            if (kind != CodeSyntaxTokens.Kind.Plain) output.Append("</span>");
        }
        output.Append(match.Groups["end"].Value);
        return output.ToString();
    }
}
