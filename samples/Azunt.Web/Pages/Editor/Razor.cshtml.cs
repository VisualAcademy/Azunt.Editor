using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Azunt.Web.Pages.Editor;

public sealed class RazorModel : PageModel
{
    private const string SessionKey = "azunt.demo.razorpages";
    private const int MaxHtmlLength = 12_000_000;

    [BindProperty] public string Html { get; set; } = "<h2>Razor Pages demo</h2><p>Try saving this content.</p>";
    public string? Notice { get; set; }

    public void OnGet() => Html = HttpContext.Session.GetString(SessionKey) ?? Html;

    public IActionResult OnPost()
    {
        Html ??= "";
        if (Html.Length > MaxHtmlLength)
        {
            ModelState.AddModelError(nameof(Html), "Document exceeds the demo size limit.");
            return Page();
        }
        HttpContext.Session.SetString(SessionKey, Html);
        Notice = "Saved in this browser session. HTML is shown below as escaped source.";
        return Page();
    }
}
