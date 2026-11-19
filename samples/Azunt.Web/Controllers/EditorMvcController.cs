using Azunt.Web.Models;
using Azunt.Editor.Export.Models;
using Microsoft.AspNetCore.Mvc;

namespace Azunt.Web.Controllers;

[Route("editor/mvc")]
public sealed class EditorMvcController(IEditorExportService exporter) : Controller
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
                ModelState.AddModelError("Export", $"Export failed: {ex.Message}");
                return View(vm);
            }
        }
        HttpContext.Session.SetString(SessionKey, vm.Html);
        vm.Notice = "Saved in this browser session. The HTML below is displayed as escaped source.";
        return View(vm);
    }
}
