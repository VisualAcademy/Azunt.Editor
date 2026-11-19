using Azunt.Web.Components;
using Azunt.Editor.Export.Services;

var builder = WebApplication.CreateBuilder(args);

// One ASP.NET Core host exercises three Azunt.Editor integration surfaces.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// In Development the sample can reuse an installed Chrome or Edge without
// downloading Playwright's version-matched Chromium. Production uses the
// standard Playwright browser unless explicitly configured by its host.
builder.Services.AddAzuntEditorExport(options =>
{
    options.BrowserChannel = builder.Configuration["Azunt:Pdf:BrowserChannel"];
    options.ExecutablePath = builder.Configuration["Azunt:Pdf:ExecutablePath"];
});

// Demo-only session persistence. Production applications supply their own document stores.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapGet("/error", () => Results.Problem("An unexpected error occurred."));

app.Run();
