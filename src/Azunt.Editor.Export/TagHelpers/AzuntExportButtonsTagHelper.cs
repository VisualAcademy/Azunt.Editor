using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Text.Encodings.Web;

namespace Azunt.Editor.Export.TagHelpers;

/// <summary>Three format submit buttons that participate in a host MVC/Razor Pages POST form.</summary>
[HtmlTargetElement("azunt-export-buttons")]
public sealed class AzuntExportButtonsTagHelper : TagHelper
{
    [HtmlAttributeName("name")]
    public string Name { get; set; } = "exportFormat";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "az-azunt-editor-export-actions");
        var name = HtmlEncoder.Default.Encode(Name);
        output.Content.SetHtmlContent(
            $"<button type=\"submit\" name=\"{name}\" value=\"Pdf\">Download PDF</button>" +
            $"<button type=\"submit\" name=\"{name}\" value=\"Docx\">Download Word</button>" +
            $"<button type=\"submit\" name=\"{name}\" value=\"Xlsx\">Download Excel</button>");
    }
}
