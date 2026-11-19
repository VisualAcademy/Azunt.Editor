using Azunt.Web.Components;
using Azunt.Editor.Export.Services;
using Azunt.Editor.Export.Models;
using Azunt.Editor.Storage;
using Azunt.Web.Images;
using Microsoft.AspNetCore.Antiforgery;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// One ASP.NET Core host exercises three Azunt.Editor integration surfaces.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// In Development the sample can reuse an installed Chrome or Edge without
// downloading Playwright's version-matched Chromium. Production uses the
// standard Playwright browser unless explicitly configured by its host.
// Optional development-only file storage. Never publish this unauthenticated
// example as an image endpoint in production.
builder.Services.AddSingleton(_ => new FileSystemEditorImageStore(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data", "editor-images"),
    "/editor/demo-images"));
builder.Services.AddSingleton<IEditorImageResolver, DemoImageResolver>();
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

// Example endpoints are intentionally unavailable outside local Development.
// A production host must provide authentication, authorization, quotas, and
// tenant/document access checks around its own IEditorImageStore endpoints.
if (app.Environment.IsDevelopment())
{
    static bool IsLoopback(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return address is not null && IPAddress.IsLoopback(address);
    }

    app.MapGet("/editor/demo-images/token", (HttpContext context, IAntiforgery antiforgery) =>
    {
        if (!IsLoopback(context)) return Results.NotFound();
        var token = antiforgery.GetAndStoreTokens(context).RequestToken;
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { requestToken = token });
    });

    app.MapPost("/editor/demo-images", async (HttpContext context, IAntiforgery antiforgery,
        FileSystemEditorImageStore store, CancellationToken cancellationToken) =>
    {
        if (!IsLoopback(context)) return Results.NotFound();
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid request token."); }
        if (!context.Request.HasFormContentType) return Results.BadRequest("Expected multipart form data.");
        try
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length is <= 0 or > FileSystemEditorImageStore.MaximumImageBytes)
                return Results.BadRequest("Invalid image size.");
            await using var content = file.OpenReadStream();
            var result = await store.SaveAsync(content, file.FileName, file.ContentType, cancellationToken);
            return Results.Json(new { url = result.Url, altText = result.AltText });
        }
        catch (ArgumentException) { return Results.BadRequest("Unsupported image content."); }
        catch (InvalidDataException) { return Results.BadRequest("Invalid multipart upload."); }
    }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(3_000_000));

    app.MapGet("/editor/demo-images/{id}", async (string id, HttpContext context,
        FileSystemEditorImageStore store, CancellationToken cancellationToken) =>
    {
        if (!IsLoopback(context)) return Results.NotFound();
        var url = "/editor/demo-images/" + id;
        if (!store.IsManagedUrl(url)) return Results.NotFound();
        var image = await store.ReadAsync(url, cancellationToken);
        if (image is null) return Results.NotFound();
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
        return Results.File(image.Content, image.ContentType);
    });
}


app.Run();
