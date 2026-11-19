using Azunt.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Azunt.Web.Controllers;

[Route("editor/mvc")]
public sealed class EditorMvcController : Controller
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
    public IActionResult Index(EditorDemoViewModel vm)
    {
        vm.Html ??= "";
        if (vm.Html.Length > MaxHtmlLength)
        {
            ModelState.AddModelError(nameof(vm.Html), "Document exceeds the demo size limit.");
            return View(vm);
        }
        HttpContext.Session.SetString(SessionKey, vm.Html);
        vm.Notice = "Saved in this browser session. The HTML below is displayed as escaped source.";
        return View(vm);
    }
}
