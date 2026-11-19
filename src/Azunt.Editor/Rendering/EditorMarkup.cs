using System.Text;
using System.Text.Encodings.Web;

namespace Azunt.Editor.Rendering;

/// <summary>Common UI markup for MVC/Razor Pages Tag Helpers and Blazor Server.</summary>
public static class EditorMarkup
{
    public static string Render(string id, string name, string? value = null, int height = 320,
        bool readOnly = false, string? uploadUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        static string Encode(string? raw) => HtmlEncoder.Default.Encode(raw ?? string.Empty);
        var safeHeight = Math.Clamp(height, 180, 1400);
        var w = new StringBuilder(6500 + (value?.Length ?? 0));
        w.Append("<div class=\"azunt-editor\" data-azunt-editor=\"true\" id=\"").Append(Encode(id))
            .Append("\" data-read-only=\"").Append(readOnly ? "true" : "false").Append('"');
        if (!string.IsNullOrWhiteSpace(uploadUrl))
            w.Append(" data-upload-url=\"").Append(Encode(uploadUrl)).Append('"');
        w.Append('>');
        w.Append("<div class=\"azunt-editor-toolbar\" role=\"toolbar\" aria-label=\"Editor toolbar\">");

        Group("Text formatting");
        Button("bold", "B", "Bold", "Ctrl+B");
        Button("italic", "I", "Italic", "Ctrl+I");
        Button("underline", "U", "Underline", "Ctrl+U");
        Button("strike", "S̶", "Strikethrough");
        Button("sup", "x²", "Superscript");
        Button("sub", "x₂", "Subscript");
        EndGroup();

        Group("Font and color");
        Select("fontFamily", "Font family", new[] {
            ("", "Font"), ("Arial", "Arial"), ("Georgia", "Georgia"),
            ("Times New Roman", "Times New Roman"), ("Verdana", "Verdana"),
            ("Segoe UI", "Segoe UI"), ("Noto Sans KR", "Noto Sans KR"),
            ("Malgun Gothic", "Malgun Gothic")
        });
        Select("fontSize", "Font size", new[] {
            ("", "Size"), ("10", "10"), ("12", "12"), ("14", "14"),
            ("16", "16"), ("18", "18"), ("20", "20"), ("24", "24"),
            ("28", "28"), ("32", "32"), ("36", "36"), ("48", "48")
        });
        Color("color", "Text color", "#1e293b");
        Color("backgroundColor", "Text highlight", "#fff2a8");
        EndGroup();

        Group("Paragraph");
        Button("h2", "H2", "Heading 2");
        Button("p", "P", "Paragraph");
        Button("ul", "• List", "Bulleted list");
        Button("ol", "1. List", "Numbered list");
        Button("left", "⇤", "Align left");
        Button("center", "≡", "Align center");
        Button("right", "⇥", "Align right");
        Button("justify", "☷", "Justify");
        Button("indent", "→", "Increase indent");
        Button("outdent", "←", "Decrease indent");
        EndGroup();

        Group("Link");
        Button("link", "🔗", "Insert or edit hyperlink");
        Button("unlink", "⊘", "Remove hyperlink");
        EndGroup();

        Group("Table");
        Button("table", "▦", "Insert 2 × 2 table");
        Button("table-row-after", "+Row", "Insert row below current cell");
        Button("table-row-delete", "−Row", "Delete current row");
        Button("table-col-after", "+Col", "Insert column after current cell");
        Button("table-col-delete", "−Col", "Delete current column");
        Button("table-delete", "×Tbl", "Delete table");
        EndGroup();

        Group("Image");
        Button("image", "▧", "Insert images");
        w.Append("<label class=\"azunt-editor-input-label\" title=\"Selected image width in pixels\">W ")
            .Append("<input class=\"azunt-editor-width\" data-image-width type=\"number\" min=\"24\" max=\"2500\" step=\"1\" ")
            .Append("placeholder=\"px\" aria-label=\"Image width\" /></label>");
        Select("imageAlign", "Image alignment", new[] { ("", "Image align"), ("left", "Left"), ("center", "Center"), ("right", "Right") });
        Button("image-delete", "×Img", "Delete selected image");
        EndGroup();

        Group("Document");
        Button("undo", "↶", "Undo", "Ctrl+Z");
        Button("redo", "↷", "Redo", "Ctrl+Y");
        Button("source", "</>", "Edit HTML source");
        Button("preview", "▤", "Print preview");
        Button("print", "⎙", "Print document");
        Select("paper", "Paper size", new[] { ("A4", "A4"), ("Letter", "Letter"), ("Legal", "Legal") });
        Select("orientation", "Page orientation", new[] { ("portrait", "Portrait"), ("landscape", "Landscape") });
        Select("margin", "Page margins", new[] { ("normal", "Normal margins"), ("narrow", "Narrow margins"), ("wide", "Wide margins") });
        EndGroup();
        w.Append("</div>");

        w.Append("<div class=\"azunt-editor-link-panel\" data-link-panel hidden role=\"group\" aria-label=\"Edit hyperlink\">")
            .Append("<label>Text <input type=\"text\" data-link-text maxlength=\"250\" autocomplete=\"off\"></label>")
            .Append("<label>URL <input type=\"url\" data-link-url placeholder=\"https://example.com\" maxlength=\"2048\" autocomplete=\"off\"></label>")
            .Append("<button type=\"button\" data-link-apply>Apply link</button>")
            .Append("<button type=\"button\" data-link-cancel>Cancel</button></div>");

        w.Append("<div class=\"azunt-editor-canvas\" role=\"textbox\" aria-label=\"Document content\" aria-multiline=\"true\" contenteditable=\"")
            .Append(readOnly ? "false" : "true")
            .Append("\" style=\"min-height:").Append(safeHeight).Append("px\"></div>");
        w.Append("<textarea class=\"azunt-editor-source\" data-azunt-source hidden aria-label=\"HTML source\" spellcheck=\"false\" style=\"min-height:")
            .Append(safeHeight).Append("px\"></textarea>");
        w.Append("<textarea hidden class=\"azunt-editor-value\" data-azunt-value=\"true\" name=\"")
            .Append(Encode(name)).Append("\">").Append(Encode(value)).Append("</textarea>");
        w.Append("<input hidden type=\"file\" accept=\"image/png,image/jpeg,image/gif,image/webp\" multiple data-azunt-images=\"true\" aria-label=\"Select images\" />");
        w.Append("<p class=\"azunt-editor-message\" role=\"status\" aria-live=\"polite\"></p>");
        w.Append("</div>");
        return w.ToString();

        void Group(string label) => w.Append("<div class=\"azunt-editor-group\" role=\"group\" aria-label=\"").Append(Encode(label)).Append("\">");
        void EndGroup() => w.Append("</div>");
        void Button(string command, string label, string title, string? shortcut = null)
        {
            w.Append("<button type=\"button\" class=\"azunt-editor-button\" data-command=\"").Append(Encode(command))
                .Append("\" title=\"").Append(Encode(shortcut is null ? title : $"{title} ({shortcut})"))
                .Append("\" aria-label=\"").Append(Encode(title)).Append("\">").Append(Encode(label)).Append("</button>");
        }
        void Color(string command, string title, string initial)
        {
            w.Append("<label class=\"azunt-editor-input-label\" title=\"").Append(Encode(title)).Append("\">")
                .Append(command == "color" ? "A " : "▣ ")
                .Append("<input type=\"color\" data-select=\"").Append(Encode(command))
                .Append("\" value=\"").Append(initial).Append("\" aria-label=\"").Append(Encode(title)).Append("\" /></label>");
        }
        void Select(string command, string title, (string Value, string Label)[] options)
        {
            w.Append("<select class=\"azunt-editor-select\" data-select=\"").Append(Encode(command))
                .Append("\" title=\"").Append(Encode(title)).Append("\" aria-label=\"").Append(Encode(title)).Append("\">");
            foreach (var (optionValue, optionLabel) in options)
                w.Append("<option value=\"").Append(Encode(optionValue)).Append("\">").Append(Encode(optionLabel)).Append("</option>");
            w.Append("</select>");
        }
    }
}
