using Azunt.Editor.Rendering;
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

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.Content.SetHtmlContent(EditorMarkup.Render(
            Id ?? $"azunt-editor-{Guid.NewGuid():N}", Name, Value, Height, ReadOnly, UploadUrl));
    }
}
