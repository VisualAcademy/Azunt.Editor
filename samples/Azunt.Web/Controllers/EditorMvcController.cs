using Azunt.Web.Models;
using Azunt.Editor.Export.Models;
using Azunt.Editor.Export.Html;
using Microsoft.AspNetCore.Mvc;

namespace Azunt.Web.Controllers;

[Route("editor/mvc")]
public sealed class EditorMvcController(IEditorExportService exporter, ILogger<EditorMvcController> logger) : Controller
{
    private const string SessionKey = "azunt.demo.mvc";
    private const int MaxHtmlLength = 12_000_000;

    [HttpGet]
    public IActionResult Index()
    {
        var vm = new EditorDemoViewModel();
        vm.Html = HttpContext.Session.GetString(SessionKey) ?? vm.Html;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(14_000_000)]
    public async Task<IActionResult> Index(EditorDemoViewModel vm, string? exportFormat, CancellationToken cancellationToken)
    {
        vm.Html ??= "";
        if (vm.Html.Length > MaxHtmlLength)
        {
            ModelState.AddModelError(nameof(vm.Html), "Document exceeds the demo size limit.");
            return View(vm);
        }
        try { vm.Html = EditorHtmlSanitizer.Sanitize(vm.Html); }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(vm.Html), "Document contains unsupported or excessively complex HTML.");
            return View(vm);
        }
        if (!string.IsNullOrWhiteSpace(exportFormat))
        {
            if (!Enum.TryParse<EditorExportFormat>(exportFormat, true, out var format))
                return BadRequest("Unsupported export format.");
            try
            {
                var exported = await exporter.ExportAsync(vm.Html, format, new EditorExportOptions
                {
                    FileName = "Azunt-MVC-Report", Title = "MVC editor report",
                    PaperSize = vm.PaperSize, Orientation = vm.Orientation, Margin = vm.Margin
                }, cancellationToken);
                return File(exported.Content, exported.ContentType, exported.FileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "MVC editor export failed for format {Format}.", format);
                ModelState.AddModelError("Export", "Export failed. Check the application logs for details.");
                return View(vm);
            }
        }
        HttpContext.Session.SetString(SessionKey, vm.Html);
        vm.Notice = "Saved in this browser session. The HTML below is displayed as escaped source.";
        return View(vm);
    }
}
