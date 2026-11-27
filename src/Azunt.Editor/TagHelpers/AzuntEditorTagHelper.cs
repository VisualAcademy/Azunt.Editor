using Azunt.Editor.Rendering;
using Azunt.Editor.Models;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Azunt.Editor.TagHelpers;

/// <summary>Shared HTML editor for MVC views and Razor Pages.</summary>
[HtmlTargetElement("azunt-editor")]
public sealed class AzuntEditorTagHelper : TagHelper
{
    [HtmlAttributeName("id")]
    public string? Id { get; set; }

    [HtmlAttributeName("name")]
    public string Name { get; set; } = "Html";

    [HtmlAttributeName("value")]
    public string? Value { get; set; }

    [HtmlAttributeName("height")]
    public int Height { get; set; } = 320;

    [HtmlAttributeName("read-only")]
    public bool ReadOnly { get; set; }

    [HtmlAttributeName("upload-url")]
    public string? UploadUrl { get; set; }

    [HtmlAttributeName("toolbar-mode")]
    public string ToolbarMode { get; set; } = "Compact";

    /// <summary>Hide the Compact toolbar's top menu row while keeping quick tools and More.</summary>
    [HtmlAttributeName("show-menu-bar")]
    public bool ShowMenuBar { get; set; } = true;

    /// <summary>Display toolbar group headings; More panel headings remain visible.</summary>
    [HtmlAttributeName("show-toolbar-group-labels")]
    public bool ShowToolbarGroupLabels { get; set; } = true;

    /// <summary>Edit (default) or View. View hides all editor controls.</summary>
    [HtmlAttributeName("mode")]
    public string Mode { get; set; } = "Edit";

    /// <summary>On-screen document style; Standard is the default.</summary>
    [HtmlAttributeName("content-rendering")]
    public string ContentRendering { get; set; } = "Standard";

    /// <summary>Enable the optional document style selector.</summary>
    [HtmlAttributeName("show-document-style-selector")]
    public bool ShowDocumentStyleSelector { get; set; }

    [HtmlAttributeName("custom-document-styles")]
    public IReadOnlyList<EditorDocumentStyle>? CustomDocumentStyles { get; set; }

    [HtmlAttributeName("document-style-id")]
    public string? DocumentStyleId { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!Enum.TryParse<EditorMode>(Mode, true, out var mode) || !Enum.IsDefined(mode))
            throw new ArgumentException("Azunt.Editor mode must be Edit or View.", nameof(Mode));
        if (!Enum.TryParse<EditorContentRendering>(ContentRendering, true, out var contentRendering) || !Enum.IsDefined(contentRendering))
            throw new ArgumentException("Unknown Azunt.Editor content rendering style.", nameof(ContentRendering));
        output.TagName = null;
        output.Content.SetHtmlContent(EditorMarkup.Render(
            Id ?? $"azunt-editor-{Guid.NewGuid():N}", Name, Value, Height, ReadOnly, UploadUrl, ToolbarMode, mode, ShowMenuBar, ShowToolbarGroupLabels, contentRendering, ShowDocumentStyleSelector, CustomDocumentStyles, DocumentStyleId));
    }
}
