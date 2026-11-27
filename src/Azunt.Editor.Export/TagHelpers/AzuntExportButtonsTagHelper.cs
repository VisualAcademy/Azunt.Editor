using Microsoft.AspNetCore.Razor.TagHelpers;
using Azunt.Editor.Export.Models;
using System.Text.Encodings.Web;

namespace Azunt.Editor.Export.TagHelpers;

/// <summary>Three format submit buttons that participate in a host MVC/Razor Pages POST form.</summary>
[HtmlTargetElement("azunt-export-buttons")]
public sealed class AzuntExportButtonsTagHelper : TagHelper
{
    [HtmlAttributeName("name")]
    public string Name { get; set; } = "exportFormat";

    /// <summary>Optional editor in the same POST form; exposes the same buttons in its Export menu.</summary>
    [HtmlAttributeName("editor-id")]
    public string? EditorId { get; set; }

    [HtmlAttributeName("formats")]
    public EditorExportFormats Formats { get; set; } = EditorExportFormats.All;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "az-azunt-editor-export-actions");
        if (!string.IsNullOrWhiteSpace(EditorId))
            output.Attributes.SetAttribute("data-azunt-export-editor", EditorId);
        var name = HtmlEncoder.Default.Encode(Name);
        output.Content.SetHtmlContent(string.Concat(EditorExportMenu.GetCommands(Formats).Select(command =>
            $"<button type=\"submit\" name=\"{name}\" value=\"{command.Format}\" data-azunt-export-command=\"{command.Id}\">{command.Label}</button>")));
    }
}
