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

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!Enum.TryParse<EditorMode>(Mode, true, out var mode) || !Enum.IsDefined(mode))
            throw new ArgumentException("Azunt.Editor mode must be Edit or View.", nameof(Mode));
        output.TagName = null;
        output.Content.SetHtmlContent(EditorMarkup.Render(
            Id ?? $"azunt-editor-{Guid.NewGuid():N}", Name, Value, Height, ReadOnly, UploadUrl, ToolbarMode, mode, ShowMenuBar, ShowToolbarGroupLabels));
    }
}
