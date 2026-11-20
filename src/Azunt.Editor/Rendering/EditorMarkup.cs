using System.Text;
using System.Text.Encodings.Web;
using Azunt.Editor.Models;

namespace Azunt.Editor.Rendering;

/// <summary>Common UI markup for MVC/Razor Pages Tag Helpers and Blazor Server.</summary>
public static class EditorMarkup
{
    public static string Render(string id, string name, string? value = null, int height = 320,
        bool readOnly = false, string? uploadUrl = null, string toolbarMode = "Compact", EditorMode mode = EditorMode.Edit)
        => RenderCore(id, name, value, height, readOnly, uploadUrl, toolbarMode, mode, manualMount: false);

    // Blazor prerendering leaves this markup static until its interactive
    // OnAfterRenderAsync callback binds the JS engine and its .NET reference.
    // Auto-mounting during DOMContentLoaded changes prerendered nodes before
    // Blazor has attached, which can disrupt hydration and command binding.
    internal static string RenderForBlazor(string id, string name, string? value, int height,
        bool readOnly, string? uploadUrl, string toolbarMode, EditorMode mode)
        => RenderCore(id, name, value, height, readOnly, uploadUrl, toolbarMode, mode, manualMount: true);

    private static string RenderCore(string id, string name, string? value, int height,
        bool readOnly, string? uploadUrl, string toolbarMode, EditorMode mode, bool manualMount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var viewer = mode == EditorMode.View;

        static string Encode(string? raw) => HtmlEncoder.Default.Encode(raw ?? string.Empty);
        var safeHeight = Math.Clamp(height, 180, 1400);
        var w = new StringBuilder(6500 + (value?.Length ?? 0));
        w.Append("<div class=\"az-azunt-editor\" data-azunt-editor=\"true\" id=\"").Append(Encode(id))
            .Append("\" data-read-only=\"").Append(readOnly ? "true" : "false").Append('"')
            .Append(" data-editor-mode=\"").Append(viewer ? "view" : "edit").Append('"');
        if (manualMount) w.Append(" data-azunt-mount=\"manual\"");
        if (!string.IsNullOrWhiteSpace(uploadUrl))
            w.Append(" data-upload-url=\"").Append(Encode(uploadUrl)).Append('"');
        w.Append(" data-toolbar-mode=\"").Append(string.Equals(toolbarMode, "Classic", StringComparison.OrdinalIgnoreCase) ? "classic" : "compact").Append("\">");
        w.Append("<div class=\"az-azunt-editor-toolbar\" role=\"toolbar\" aria-label=\"Editor toolbar\">");
        w.Append("<div class=\"az-azunt-editor-menu-row\" data-compact-menu role=\"menubar\" aria-label=\"Editor menus\">");
        foreach (var key in new[] { "File", "Edit", "View", "Insert", "Format", "Table", "Tools", "Export" })
            w.Append("<button type=\"button\" class=\"az-azunt-editor-menu-trigger\" data-azunt-menu=\"").Append(key.ToLowerInvariant())
                .Append("\" aria-expanded=\"false\" aria-haspopup=\"dialog\">").Append(key).Append("</button>");
        w.Append("</div><div class=\"az-azunt-editor-quick-row\" data-compact-quick role=\"group\" aria-label=\"Common formatting\">");
        int quickIndex = 0;
        foreach (var (command, label, title, optional) in new (string Command, string Label, string Title, bool Optional)[] {
            ("undo", "↶", "Undo", false), ("redo", "↷", "Redo", false),
            ("bold", "B", "Bold", false), ("italic", "I", "Italic", false), ("underline", "U", "Underline", false),
            ("ul", "•", "Bulleted list", true), ("ol", "1.", "Numbered list", true),
            ("link", "🔗", "Link", true), ("image", "▧", "Insert image", true), ("table", "▦", "Insert table", true),
            ("find", "⌕", "Find", true) }) {
            if (quickIndex is 0 or 2 or 5 or 7 or 10) {
                if (quickIndex > 0) w.Append("</span></span>");
                var labelText = quickIndex switch { 0 => "History", 2 => "Text formatting", 5 => "Lists", 7 => "Insert", _ => "Tools" };
                w.Append("<span class=\"az-azunt-editor-quick-group")
                    .Append(quickIndex >= 5 ? " az-azunt-editor-quick-group-optional" : "")
                    .Append("\"><span class=\"az-azunt-editor-quick-label\">").Append(labelText)
                    .Append("</span><span class=\"az-azunt-editor-quick-controls\">");
            }
            if (command == "table") {
                // Keep the existing 2x2 shortcut while exposing a CKEditor-style
                // grid picker directly beneath the toolbar button.
                TableSplit(compact: true);
                FormSplit(compact: true);
                CodeSplit(compact: true);
                quickIndex++;
                continue;
            }
            w.Append("<button type=\"button\" class=\"az-azunt-editor-button az-azunt-editor-quick-command")
                .Append(optional ? " az-azunt-editor-quick-optional" : "")
                .Append("\" data-quick-command=\"").Append(command).Append("\" title=\"").Append(Encode(title))
                .Append("\" aria-label=\"").Append(Encode(title)).Append("\"");
            if (command is "bold" or "italic" or "underline" or "ul" or "ol") w.Append(" aria-pressed=\"false\"");
            w.Append(">");
            w.Append(Icon(command) is { } icon ? "<span class=\"az-azunt-editor-button-icon\" aria-hidden=\"true\">" + icon + "</span>" : Encode(label));
            w.Append("</button>");
            quickIndex++;
        }
        w.Append("</span></span>");
        w.Append("<span class=\"az-azunt-editor-quick-custom\" data-quick-custom></span>")
            .Append("<button class=\"az-azunt-editor-menu-trigger az-azunt-editor-quick-more\" type=\"button\" data-azunt-menu=\"more\" aria-expanded=\"false\" aria-haspopup=\"dialog\" title=\"More editor commands\" aria-label=\"More editor commands\">More ⋯</button>");
        w.Append("</div><div class=\"az-azunt-editor-classic-groups\" data-classic-groups>");

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

        Group("Form controls");
        FormSplit(compact: false);
        EndGroup();

        Group("Table");
        TableSplit(compact: false);
        Button("table-row-before", "↑ Row", "Insert row above current cell");
        Button("table-row-after", "+Row", "Insert row below current cell");
        Button("table-row-delete", "−Row", "Delete current row");
        Button("table-col-before", "← Col", "Insert column before current cell");
        Button("table-col-after", "+Col", "Insert column after current cell");
        Button("table-col-delete", "−Col", "Delete current column");
        Button("table-delete", "×Tbl", "Delete table");
        Button("table-merge-right", "Merge →", "Merge selected cell with the cell to its right");
        Button("table-split", "Split", "Split horizontally merged cell");
        Button("table-header-toggle", "Header", "Toggle current row between header and data cells");
        Color("tableBorderColor", "Table border color", "#aeb8c6");
        Select("tableBorderWidth", "Table border thickness", new[] {
            ("", "Border px"), ("1", "1 px"), ("2", "2 px"), ("3", "3 px"), ("4", "4 px")
        });
        EndGroup();

        Group("Image");
        Button("image", "▧", "Insert images");
        Select("imageInsertSize", "New image insertion size", new[] { ("original", "Original size"), ("480", "480 px") });
        Button("image-original", "1:1", "Restore selected image to its original dimensions");
        w.Append("<label class=\"az-azunt-editor-input-label\" title=\"Selected image width in pixels (blank means original size)\">W ")
            .Append("<input class=\"az-azunt-editor-width\" data-image-width type=\"number\" min=\"24\" max=\"2500\" step=\"1\" ")
            .Append("placeholder=\"original\" aria-label=\"Image width\" /></label>");
        w.Append("<label class=\"az-azunt-editor-input-label\" title=\"Description of selected image\">Alt ")
            .Append("<input class=\"az-azunt-editor-alt\" data-image-alt type=\"text\" maxlength=\"200\" ")
            .Append("placeholder=\"Image description\" aria-label=\"Image alternative text\" /></label>");
        Select("imageAlign", "Image alignment", new[] { ("", "Image align"), ("left", "Left"), ("center", "Center"), ("right", "Right") });
        Button("image-delete", "×Img", "Delete selected image");
        EndGroup();

        Group("Writing tools");
        CodeSplit(compact: false);
        Button("find", "Find", "Find and replace", "Ctrl+F");
        Button("clear-format", "Tx", "Remove formatting from selected text");
        Button("horizontal-rule", "―", "Insert horizontal line");
        Button("page-break", "Page", "Insert page break for print and Word");
        Button("special-chars", "Ω", "Insert special character");
        Button("fullscreen", "⛶", "Toggle fullscreen", "Esc to exit");
        Button("datetime", "Date", "Insert date or time");
        Button("signature", "Sign", "Draw and insert a signature image");
        Button("templates", "Template", "Open document templates");
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
        w.Append("</div>"); // Classic groups remain available to Compact menus.
        w.Append("<div class=\"az-azunt-editor-menu-panel\" data-menu-panel hidden role=\"dialog\" aria-modal=\"false\" tabindex=\"-1\" aria-label=\"Editor menu commands\"></div>");
        w.Append("<div class=\"az-azunt-editor-context-tools\" data-context-tools hidden role=\"toolbar\" aria-label=\"Selected item tools\"></div>");
        w.Append("</div>");

        w.Append("<div class=\"az-code-panel\" data-code-panel hidden role=\"group\" aria-label=\"Insert or edit code block\">")
            .Append("<label>Language <select data-code-language aria-label=\"Code language\">")
            .Append("<option value=\"csharp\">C#</option><option value=\"javascript\">JavaScript</option>")
            .Append("<option value=\"sql\">SQL</option><option value=\"html\">HTML</option><option value=\"css\">CSS</option>")
            .Append("</select></label><label>Code <textarea data-code-text rows=\"9\" maxlength=\"100000\" spellcheck=\"false\" aria-label=\"Code text\"></textarea></label>")
            .Append("<div class=\"az-code-panel-actions\"><button type=\"button\" data-code-apply>Apply code</button>")
            .Append("<button type=\"button\" data-code-cancel>Cancel</button></div></div>");
        w.Append("<div class=\"az-azunt-editor-link-panel\" data-link-panel hidden role=\"group\" aria-label=\"Edit hyperlink\">")
            .Append("<label>Text <input type=\"text\" data-link-text maxlength=\"250\" autocomplete=\"off\"></label>")
            .Append("<label>URL <input type=\"url\" data-link-url placeholder=\"https://example.com\" maxlength=\"2048\" autocomplete=\"off\"></label>")
            .Append("<button type=\"button\" data-link-apply>Apply link</button>")
            .Append("<button type=\"button\" data-link-cancel>Cancel</button></div>");

        w.Append("<div class=\"az-azunt-editor-find-panel\" data-find-panel hidden role=\"search\" aria-label=\"Find and replace\">")
            .Append("<label>Find <input type=\"text\" data-find-query maxlength=\"200\" autocomplete=\"off\"></label>")
            .Append("<label>Replace <input type=\"text\" data-find-replacement maxlength=\"200\" autocomplete=\"off\"></label>")
            .Append("<button type=\"button\" data-find-next>Find next</button>")
            .Append("<button type=\"button\" data-find-replace>Replace</button>")
            .Append("<button type=\"button\" data-find-all>Replace all</button>")
            .Append("<button type=\"button\" data-find-close>Close</button>")
            .Append("<span class=\"az-azunt-editor-find-status\" data-find-status aria-live=\"polite\"></span></div>");
        w.Append("<div class=\"az-azunt-editor-chars-panel\" data-chars-panel hidden role=\"group\" aria-label=\"Insert special character\">")
            .Append("<span class=\"az-azunt-editor-panel-heading\">Insert character</span>");
        foreach (var ch in new[] { "©", "®", "™", "€", "£", "¥", "—", "–", "•", "§", "±", "°", "µ", "∞", "≠", "≤", "≥", "α", "β", "→", "←", "✓", "★", "·" })
            w.Append("<button type=\"button\" data-insert-character=\"").Append(Encode(ch)).Append("\" aria-label=\"Insert ").Append(Encode(ch)).Append("\">")
                .Append(Encode(ch)).Append("</button>");
        w.Append("<button type=\"button\" data-chars-close>Close</button></div>");

        w.Append("<div class=\"az-azunt-editor-signature-panel\" data-signature-panel hidden role=\"group\" aria-label=\"Draw signature\">")
            .Append("<span class=\"az-azunt-editor-panel-heading\">Draw your signature using mouse, pen or touch</span>")
            .Append("<canvas data-signature-canvas width=\"640\" height=\"180\" aria-label=\"Signature drawing surface\"></canvas>")
            .Append("<label>Ink <input type=\"color\" data-signature-color value=\"#1e293b\" aria-label=\"Signature ink color\"></label>")
            .Append("<label>Pen <select data-signature-width aria-label=\"Signature pen thickness\"><option value=\"2\">Fine</option><option value=\"3\" selected>Normal</option><option value=\"5\">Thick</option></select></label>")
            .Append("<button type=\"button\" data-signature-clear>Clear</button>")
            .Append("<span class=\"az-azunt-editor-signature-note\">Handwritten image only; not a verified digital signature.</span>")
            .Append("<button type=\"button\" data-signature-insert>Insert signature</button>")
            .Append("<button type=\"button\" data-signature-close>Cancel</button></div>");

        w.Append("<div class=\"az-azunt-editor-template-panel\" data-template-panel hidden role=\"group\" aria-label=\"Document templates\">")
            .Append("<label>Template <select data-template-list aria-label=\"Choose document template\">")
            .Append("<option value=\"blank\">Blank document</option>")
            .Append("<option value=\"meeting\">Meeting notes</option>")
            .Append("<option value=\"report\">Simple report</option>")
            .Append("<option value=\"letter\">Letter</option>")
            .Append("</select></label>")
            .Append("<button type=\"button\" data-template-apply>Load template</button>")
            .Append("<button type=\"button\" data-template-close>Cancel</button></div>");

        w.Append("<div class=\"az-azunt-editor-date-panel\" data-date-panel hidden role=\"group\" aria-label=\"Insert date or time\">")
            .Append("<button type=\"button\" data-date-format=\"date\">Date</button>")
            .Append("<button type=\"button\" data-date-format=\"time\">Time</button>")
            .Append("<button type=\"button\" data-date-format=\"datetime\">Date &amp; time</button>")
            .Append("<button type=\"button\" data-date-close>Cancel</button></div>");

        w.Append("<div class=\"az-azunt-editor-canvas\" role=\"")
            .Append(viewer ? "document" : "textbox")
            .Append("\" aria-label=\"").Append(viewer ? "Document viewer" : "Document content")
            .Append(viewer ? "" : "\" aria-multiline=\"true")
            .Append("\" contenteditable=\"")
            .Append(readOnly || viewer ? "false" : "true")
            .Append('"');
        if (!viewer) w.Append(" style=\"min-height:").Append(safeHeight).Append("px\"");
        w.Append(">");
        if (viewer) w.Append(EditorViewerSanitizer.Sanitize(value));
        w.Append("</div>");
        w.Append("<div class=\"az-azunt-editor-image-resize-handle\" data-image-resize-handle hidden "
            + "role=\"button\" tabindex=\"0\" aria-label=\"Resize selected image by dragging the corner\" "
            + "title=\"Drag to resize the selected image (aspect ratio preserved)\"></div>");
        // Table grips are rendered by the host, not dynamically added to the
        // Blazor-owned DOM. Both live outside contenteditable and saved HTML.
        w.Append("<button type=\"button\" class=\"az-azunt-editor-table-move-handle\" data-table-move-handle hidden "
            + "aria-label=\"Move table\" title=\"Drag to move this table; Alt+Up/Down to move one block\">&#10021;</button>");
        w.Append("<button type=\"button\" class=\"az-azunt-editor-table-column-handle\" data-table-column-handle hidden "
            + "aria-label=\"Resize table column\" title=\"Drag to resize adjacent table columns\"></button>");
        w.Append("<div class=\"az-azunt-editor-table-drop-marker\" data-table-drop-marker hidden aria-hidden=\"true\"></div>");
        w.Append("<textarea class=\"az-azunt-editor-source\" data-azunt-source hidden aria-label=\"HTML source\" spellcheck=\"false\" style=\"min-height:")
            .Append(safeHeight).Append("px\"></textarea>");
        w.Append("<textarea hidden class=\"az-azunt-editor-value\" data-azunt-value=\"true\" name=\"")
            .Append(Encode(name)).Append('"');
        if (viewer) w.Append(" disabled"); // A viewer must not silently submit the displayed document.
        w.Append('>').Append(Encode(value)).Append("</textarea>");
        w.Append("<input hidden type=\"file\" accept=\"image/png,image/jpeg,image/gif,image/webp\" multiple data-azunt-images=\"true\" aria-label=\"Select images\" />");
        w.Append("<div class=\"az-azunt-editor-footer\"><p class=\"az-azunt-editor-message\" role=\"status\" aria-live=\"polite\" aria-atomic=\"true\"></p>")
            .Append("<span class=\"az-azunt-editor-current-format\" data-current-format aria-label=\"Current text formatting\" aria-live=\"off\">Paragraph</span>")
            .Append("<span class=\"az-azunt-editor-stats\" data-editor-stats aria-live=\"off\">0 words · 0 characters</span></div>");
        w.Append("</div>");
        return w.ToString();

        void TableSplit(bool compact)
        {
            var commandAttribute = compact ? "data-quick-command" : "data-command";
            var quickClass = compact ? " az-azunt-editor-quick-optional" : "";
            w.Append("<span class=\"az-azunt-editor-tool-split az-azunt-editor-table-split")
                .Append(quickClass).Append("\" role=\"group\" aria-label=\"Insert table\">")
                .Append("<button type=\"button\" class=\"az-azunt-editor-button az-azunt-editor-table-primary")
                .Append(compact ? " az-azunt-editor-quick-command" : "")
                .Append("\" ").Append(commandAttribute)
                .Append("=\"table\" title=\"Insert 2 × 2 table\" aria-label=\"Insert 2 by 2 table\">")
                .Append("<span class=\"az-azunt-editor-button-icon\" aria-hidden=\"true\">")
                .Append(Icon("table")).Append("</span></button>")
                .Append("<button type=\"button\" class=\"az-azunt-editor-button az-azunt-editor-table-dropdown\" ")
                .Append("data-azunt-menu=\"tablepicker\" aria-haspopup=\"dialog\" aria-expanded=\"false\" ")
                .Append("aria-label=\"Choose table rows and columns\" title=\"Choose table size\">▾</button></span>");
        }

        void FormSplit(bool compact)
        {
            var commandAttribute = compact ? "data-quick-command" : "data-command";
            w.Append("<span class=\"az-azunt-editor-tool-split az-azunt-editor-form-split\" role=\"group\" aria-label=\"Insert HTML form control\">")
                .Append("<button type=\"button\" class=\"az-azunt-editor-button az-azunt-editor-form-primary\" ")
                .Append(commandAttribute).Append("=\"form-checkbox-checked\" title=\"Insert checked checkbox\" ")
                .Append("aria-label=\"Insert checked checkbox\">☑</button>")
                .Append("<button type=\"button\" class=\"az-azunt-editor-button az-azunt-editor-form-dropdown\" ")
                .Append("data-azunt-menu=\"controls\" aria-haspopup=\"dialog\" aria-expanded=\"false\" ")
                .Append("aria-label=\"Choose radio button or checkbox\" title=\"Choose form control\">▾</button></span>");
        }

        void CodeSplit(bool compact)
        {
            var commandAttribute = compact ? "data-quick-command" : "data-command";
            w.Append("<span class=\"az-azunt-editor-tool-split az-code-toolbar\" role=\"group\" aria-label=\"Insert code block\">")
                .Append("<button type=\"button\" class=\"az-azunt-editor-button az-code-toolbar-primary\" ")
                .Append(commandAttribute).Append("=\"code-block\" title=\"Insert C# code block\" aria-label=\"Insert C# code block\">&lt;/&gt;</button>")
                .Append("<button type=\"button\" class=\"az-azunt-editor-button az-code-toolbar-dropdown\" ")
                .Append("data-azunt-menu=\"codepicker\" aria-haspopup=\"dialog\" aria-expanded=\"false\" ")
                .Append("aria-label=\"Choose code language\" title=\"Choose code language\">▾</button></span>");
        }

        void Group(string label) => w.Append("<div class=\"az-azunt-editor-group\" role=\"group\" data-group-label=\"").Append(Encode(label)).Append("\" aria-label=\"").Append(Encode(label)).Append("\">");
        void EndGroup() => w.Append("</div>");
        void Button(string command, string label, string title, string? shortcut = null)
        {
            w.Append("<button type=\"button\" class=\"az-azunt-editor-button\" data-command=\"").Append(Encode(command))
                .Append("\" title=\"").Append(Encode(shortcut is null ? title : $"{title} ({shortcut})"))
                .Append("\" aria-label=\"").Append(Encode(title)).Append("\"");
            if (command is "bold" or "italic" or "underline" or "strike" or "sup" or "sub" or "ul" or "ol") w.Append(" aria-pressed=\"false\"");
            w.Append(">")
                .Append(Icon(command) is { } svg ? "<span class=\"az-azunt-editor-button-icon\" aria-hidden=\"true\">" + svg + "</span>" : "<span class=\"az-azunt-editor-button-label\">" + Encode(label) + "</span>")
                .Append("</button>");
        }
        // SVG icons belong to Azunt.Editor: no external icon font, CDN, or UI package.
        static string? Icon(string command)
        {
            const string open = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"17\" height=\"17\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\">";
            var shape = command switch
            {
                "ul" => "<path d=\"M9 6h12M9 12h12M9 18h12\"/><path d=\"M4 6h.01M4 12h.01M4 18h.01\"/>",
                "ol" => "<path d=\"M10 6h11M10 12h11M10 18h11M3 5h2v4M3 11h3v3H3v3h3\"/>",
                "left" => "<path d=\"M4 6h16M4 10h10M4 14h16M4 18h10\"/>",
                "center" => "<path d=\"M4 6h16M7 10h10M4 14h16M7 18h10\"/>",
                "right" => "<path d=\"M4 6h16M10 10h10M4 14h16M10 18h10\"/>",
                "justify" => "<path d=\"M4 6h16M4 10h16M4 14h16M4 18h16\"/>",
                "indent" => "<path d=\"M10 6h11M10 12h11M10 18h11M3 10l3 2-3 2\"/>",
                "outdent" => "<path d=\"M10 6h11M10 12h11M10 18h11M7 10l-3 2 3 2\"/>",
                "link" => "<path d=\"M10 13a5 5 0 0 0 7.1 0l3-3a5 5 0 0 0-7.1-7.1l-1.7 1.7M14 11a5 5 0 0 0-7.1 0l-3 3a5 5 0 0 0 7.1 7.1l1.7-1.7\"/>",
                "unlink" => "<path d=\"M10 13a5 5 0 0 0 7.1 0l3-3a5 5 0 0 0-7.1-7.1M14 11a5 5 0 0 0-7.1 0l-3 3a5 5 0 0 0 7.1 7.1M3 3l18 18\"/>",
                "table" => "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"1\"/><path d=\"M3 9h18M3 15h18M9 3v18M15 3v18\"/>",
                "image" => "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><circle cx=\"9\" cy=\"9\" r=\"2\"/><path d=\"m21 16-5-5L5 21\"/>",
                "undo" => "<path d=\"M3 7v6h6M21 17a8 8 0 0 0-14-6l-4 2\"/>",
                "redo" => "<path d=\"M21 7v6h-6M3 17a8 8 0 0 1 14-6l4 2\"/>",
                "find" => "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m16 16 5 5\"/>",
                "clear-format" => "<path d=\"M4 6h13M10 6l-3 12M6 18h11M4 4l16 16\"/>",
                "horizontal-rule" => "<path d=\"M3 12h18\"/>",
                "page-break" => "<path d=\"M4 3v5m16-5v5M4 16v5m16-5v5M2 12h20M10 9l4 3-4 3\"/>",
                "special-chars" => "<path d=\"M6 20h12M4 17c-5-10 1-14 8-14s13 4 8 14M4 17h6m4 0h6\"/>",
                "fullscreen" => "<path d=\"M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5\"/>",
                "source" => "<path d=\"m8 8-4 4 4 4m8-8 4 4-4 4M14 4l-4 16\"/>",
                "preview" => "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><path d=\"M8 8h8M8 12h8M8 16h5\"/>",
                "print" => "<path d=\"M6 9V3h12v6M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M6 15h12v6H6zM18 12h.01\"/>",
                "image-delete" or "table-delete" => "<path d=\"M4 7h16M10 11v6M14 11v6M6 7l1 14h10l1-14M9 7V4h6v3\"/>",
                _ => null
            };
            return shape is null ? null : open + shape + "</svg>";
        }

        void Color(string command, string title, string initial)
        {
            w.Append("<label class=\"az-azunt-editor-input-label\" title=\"").Append(Encode(title)).Append("\">")
                .Append(command == "color" ? "A " : "▣ ")
                .Append("<input type=\"color\" data-select=\"").Append(Encode(command))
                .Append("\" value=\"").Append(initial).Append("\" aria-label=\"").Append(Encode(title)).Append("\" /></label>");
        }
        void Select(string command, string title, (string Value, string Label)[] options)
        {
            w.Append("<select class=\"az-azunt-editor-select\" data-select=\"").Append(Encode(command))
                .Append("\" title=\"").Append(Encode(title)).Append("\" aria-label=\"").Append(Encode(title));
            if (command == "paper") w.Append("\" name=\"PaperSize");
            if (command == "orientation") w.Append("\" name=\"Orientation");
            if (command == "margin") w.Append("\" name=\"Margin");
            w.Append("\">");
            foreach (var (optionValue, optionLabel) in options)
                w.Append("<option value=\"").Append(Encode(optionValue)).Append("\">").Append(Encode(optionLabel)).Append("</option>");
            w.Append("</select>");
        }
    }
}
