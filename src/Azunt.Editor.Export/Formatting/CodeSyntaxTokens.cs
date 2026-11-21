namespace Azunt.Editor.Export.Formatting;

/// <summary>
/// Bounded, non-executing lexical colors for export documents. This is not a compiler
/// or an HTML parser: its input is already sanitized code *text* from Azunt.Editor.
/// No highlighted tokens are ever written back to the editor's saved HTML.
/// </summary>
internal static class CodeSyntaxTokens
{
    internal enum Kind { Plain, Keyword, Comment, String, Number, Tag }
    internal readonly record struct Token(string Text, Kind Category);

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed",
        "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal",
        "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "partial", "private", "protected", "public", "readonly", "record", "ref", "return",
        "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "var", "virtual", "void", "volatile", "when", "where", "while", "yield"
    };
    private static readonly HashSet<string> JavaScriptKeywords = new(StringComparer.Ordinal)
    {
        "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger",
        "default", "delete", "do", "else", "export", "extends", "false", "finally", "for",
        "from", "function", "if", "import", "in", "instanceof", "let", "new", "null",
        "of", "return", "static", "super", "switch", "this", "throw", "true", "try",
        "typeof", "undefined", "var", "void", "while", "yield"
    };
    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "and", "as", "asc", "between", "by", "case", "cast", "create", "cross", "delete",
        "desc", "distinct", "drop", "else", "end", "exists", "false", "from", "full", "group",
        "having", "in", "inner", "insert", "into", "is", "join", "left", "like", "limit", "not",
        "null", "on", "or", "order", "outer", "right", "select", "set", "table", "then",
        "top", "true", "union", "update", "values", "when", "where", "with"
    };

    /// <summary>Limits token count and code length so DOCX and PDF do not explode on adversarial input.</summary>
    internal static IEnumerable<Token> Tokenize(string source, string language)
    {
        if (source.Length == 0) yield break;
        if (source.Length > 100_000 || language is not ("csharp" or "javascript" or "sql" or "html" or "css"))
        {
            yield return new Token(source, Kind.Plain);
            yield break;
        }

        var emitted = 0;
        // Avoid repeatedly scanning a very long, unterminated HTML tag sequence.
        var lastHtmlTagEnd = language == "html" ? source.LastIndexOf('>') : -1;
        for (var index = 0; index < source.Length;)
        {
            if (++emitted > 20_000)
            {
                yield return new Token(source[index..], Kind.Plain);
                yield break;
            }

            var start = index;
            var category = Kind.Plain;
            if ((language is "csharp" or "javascript") && Has("//") ||
                language == "sql" && Has("--"))
            {
                index = source.IndexOf('\n', index);
                if (index < 0) index = source.Length;
                category = Kind.Comment;
            }
            else if ((language is "csharp" or "javascript" or "css") && Has("/*"))
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 2;
                category = Kind.Comment;
            }
            else if (language == "html" && Has("<!--"))
            {
                var end = source.IndexOf("-->", index + 4, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 3;
                category = Kind.Comment;
            }
            else if (language == "html" && source[index] == '<' && index < lastHtmlTagEnd)
            {
                index = source.IndexOf('>', index + 1) + 1;
                category = Kind.Tag;
            }
            else if ((source[index] is '\'' or '"') ||
                     (language == "javascript" && source[index] == '`') ||
                     (language == "csharp" && Has("@\"")))
            {
                var verbatim = language == "csharp" && Has("@\"");
                var quote = verbatim ? '"' : source[index];
                index += verbatim ? 2 : 1;
                while (index < source.Length)
                {
                    if (!verbatim && source[index] == '\\' && index + 1 < source.Length) { index += 2; continue; }
                    if (source[index] == quote)
                    {
                        if (verbatim && index + 1 < source.Length && source[index + 1] == '"')
                        { index += 2; continue; }
                        index++;
                        break;
                    }
                    index++;
                }
                category = Kind.String;
            }
            else if (char.IsDigit(source[index]))
            {
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '.' or '_')) index++;
                category = Kind.Number;
            }
            else if (char.IsLetter(source[index]) || source[index] == '_')
            {
                index++;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_')) index++;
                var word = source[start..index];
                if (language == "csharp" && CSharpKeywords.Contains(word) ||
                    language == "javascript" && JavaScriptKeywords.Contains(word) ||
                    language == "sql" && SqlKeywords.Contains(word)) category = Kind.Keyword;
            }
            else index++;

            yield return new Token(source[start..index], category);

            bool Has(string candidate) => source.Length - index >= candidate.Length &&
                string.CompareOrdinal(source, index, candidate, 0, candidate.Length) == 0;
        }
    }

    internal static string? Color(Kind kind) => kind switch
    {
        Kind.Keyword => "7C3AED",
        Kind.Comment => "64748B",
        Kind.String => "167347",
        Kind.Number => "0969DA",
        Kind.Tag => "0550AE",
        _ => null
    };
}
