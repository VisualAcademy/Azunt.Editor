using Azunt.Editor.Storage;
using Azunt.Web.Images;
using Microsoft.AspNetCore.Mvc;
using Azunt.Editor.Export.Models;
using Azunt.Editor.Export.Html;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Azunt.Web.Pages.Editor;

public sealed class RazorModel(IEditorExportService exporter, ILogger<RazorModel> logger, FileSystemEditorImageStore imageStore,
    EditorInlineImagePersistence imagePersistence, IWebHostEnvironment environment) : PageModel
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
        var saveImagesOnSubmit = environment.IsDevelopment() &&
            string.Equals(Request.Query["images"].ToString(), "save", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(exportFormat);
        try
        {
            if (saveImagesOnSubmit)
            {
                var result = await DemoImageSaveService.PersistAsync(
                    imagePersistence, imageStore, Html, cancellationToken);
                Html = result.Html;
                Notice = $"Saved {result.SavedImageCount} image file(s). HTML remains in this browser session.";
            }
            else
                Html = EditorHtmlSanitizer.Sanitize(Html, imageStore.IsManagedUrl);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(Html), "Document or image is unsupported or exceeds the demo limits.");
            return Page();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            logger.LogError(ex, "Razor Pages final-save image persistence failed.");
            ModelState.AddModelError(nameof(Html), "Image save failed. Check the application logs.");
            return Page();
        }
        if (!string.IsNullOrWhiteSpace(exportFormat))
        {
            if (!EditorExportMenu.TryParseFormFormat(exportFormat, out var format))
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
                logger.LogError(ex, "Razor Pages editor export failed for format {Format}.", format);
                ModelState.AddModelError("Export", "Export failed. Check the application logs for details.");
                return Page();
            }
        }
        HttpContext.Session.SetString(SessionKey, Html);
        Notice ??= "Saved in this browser session. HTML is shown below as escaped source.";
        return Page();
    }
}
