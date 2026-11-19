using Microsoft.AspNetCore.Mvc;

namespace Azunt.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
