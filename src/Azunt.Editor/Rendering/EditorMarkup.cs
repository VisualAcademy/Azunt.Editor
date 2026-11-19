using System.Text;
using System.Text.Encodings.Web;

namespace Azunt.Editor.Rendering;

/// <summary>Creates the same editor markup for MVC, Razor Pages, and Blazor.</summary>
public static class EditorMarkup
{
    public static string Render(
        string id,
        string name,
        string? value = null,
        int height = 320,
        bool readOnly = false,
        string? uploadUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string Encode(string? raw) => HtmlEncoder.Default.Encode(raw ?? string.Empty);
        var safeHeight = Math.Clamp(height, 180, 1400);
        var writer = new StringBuilder(2800 + (value?.Length ?? 0));
        writer.Append("<div class=\"azunt-editor\" data-azunt-editor=\"true\" id=\"")
            .Append(Encode(id)).Append("\" data-read-only=\"")
            .Append(readOnly ? "true" : "false").Append('"');
        if (!string.IsNullOrWhiteSpace(uploadUrl))
        {
            writer.Append(" data-upload-url=\"").Append(Encode(uploadUrl)).Append('"');
        }
        writer.Append('>');
        writer.Append("<div class=\"azunt-editor-toolbar\" role=\"toolbar\" aria-label=\"Editor toolbar\">");
        AddButton("bold", "B", "Bold", "Ctrl+B");
        AddButton("italic", "I", "Italic", "Ctrl+I");
        AddButton("underline", "U", "Underline", "Ctrl+U");
        AddButton("h2", "H2", "Heading", null);
        AddButton("p", "P", "Paragraph", null);
        AddButton("ul", "• List", "Bullet list", null);
        AddButton("ol", "1. List", "Numbered list", null);
        AddButton("left", "⇤", "Align left", null);
        AddButton("center", "≡", "Align center", null);
        AddButton("right", "⇥", "Align right", null);
        AddButton("table", "▦", "Insert table", null);
        AddButton("image", "▧", "Insert images", null);
        AddButton("undo", "↶", "Undo", "Ctrl+Z");
        AddButton("redo", "↷", "Redo", "Ctrl+Y");
        AddButton("print", "⎙", "Print", null);
        writer.Append("</div><div class=\"azunt-editor-canvas\" role=\"textbox\" aria-label=\"Document content\" aria-multiline=\"true\" contenteditable=\"")
            .Append(readOnly ? "false" : "true")
            .Append("\" style=\"min-height:").Append(safeHeight).Append("px\"></div>");
        writer.Append("<textarea hidden class=\"azunt-editor-value\" data-azunt-value=\"true\" name=\"")
            .Append(Encode(name)).Append("\">").Append(Encode(value)).Append("</textarea>");
        writer.Append("<input hidden type=\"file\" accept=\"image/png,image/jpeg,image/gif,image/webp\" multiple data-azunt-images=\"true\" aria-label=\"Select images\" />");
        writer.Append("<p class=\"azunt-editor-message\" role=\"status\" aria-live=\"polite\"></p>");
        writer.Append("</div>");
        return writer.ToString();

        void AddButton(string command, string label, string title, string? shortcut)
        {
            writer.Append("<button type=\"button\" class=\"azunt-editor-button\" data-command=\"")
                .Append(Encode(command)).Append("\" title=\"").Append(Encode(title));
            if (shortcut is not null) writer.Append(" (").Append(Encode(shortcut)).Append(')');
            writer.Append("\" aria-label=\"").Append(Encode(title)).Append("\">")
                .Append(Encode(label)).Append("</button>");
        }
    }
}
