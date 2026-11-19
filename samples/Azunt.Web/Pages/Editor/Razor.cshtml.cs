using Microsoft.AspNetCore.Mvc;
using Azunt.Editor.Export.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Azunt.Web.Pages.Editor;

public sealed class RazorModel(IEditorExportService exporter) : PageModel
{
    private const string SessionKey = "azunt.demo.razorpages";
    private const int MaxHtmlLength = 12_000_000;

    [BindProperty] public string Html { get; set; } = "<h2>Razor Pages demo</h2><p>Try saving this content.</p>";
    [BindProperty] public string PaperSize { get; set; } = "A4";
    [BindProperty] public string Orientation { get; set; } = "portrait";
    [BindProperty] public string Margin { get; set; } = "normal";
    public string? Notice { get; set; }

    public void OnGet() => Html = HttpContext.Session.GetString(SessionKey) ?? Html;

    public async Task<IActionResult> OnPostAsync(string? exportFormat, CancellationToken cancellationToken)
    {
        Html ??= "";
        if (Html.Length > MaxHtmlLength)
        {
            ModelState.AddModelError(nameof(Html), "Document exceeds the demo size limit.");
            return Page();
        }
        if (!string.IsNullOrWhiteSpace(exportFormat))
        {
            if (!Enum.TryParse<EditorExportFormat>(exportFormat, true, out var format))
                return BadRequest("Unsupported export format.");
            try
            {
                var result = await exporter.ExportAsync(Html, format, new EditorExportOptions
                {
                    FileName = "Azunt-RazorPages-Report", Title = "Razor Pages editor report",
                    PaperSize = PaperSize, Orientation = Orientation, Margin = Margin
                }, cancellationToken);
                return File(result.Content, result.ContentType, result.FileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ModelState.AddModelError("Export", $"Export failed: {ex.Message}");
                return Page();
            }
        }
        HttpContext.Session.SetString(SessionKey, Html);
        Notice = "Saved in this browser session. HTML is shown below as escaped source.";
        return Page();
    }
}
