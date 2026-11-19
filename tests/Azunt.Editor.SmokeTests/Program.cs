using Azunt.Editor.Rendering;

// Package-independent console tests: no xUnit/NUnit/external test framework.
var safe = EditorMarkup.Render("edit-one", "Description", "<p>Hello</p>");
Require(safe.Contains("data-azunt-editor=\"true\""), "Root marker");
Require(safe.Contains("name=\"Description\""), "Field name");
Require(safe.Contains("&lt;p&gt;Hello&lt;/p&gt;"), "Encoded initial HTML");
Require(safe.Contains("data-azunt-images=\"true\""), "Multiple image input");
Require(safe.Contains("multiple"), "Multiple file selection");

var malicious = EditorMarkup.Render("x\" onmouseover=\"alert(1)", "Html", "</textarea><script>alert(1)</script>");
Require(!malicious.Contains("</textarea><script>", StringComparison.OrdinalIgnoreCase), "Initial value safely encoded");
Require(!malicious.Contains("id=\"x\" onmouseover", StringComparison.Ordinal), "ID safely encoded");
Require(malicious.Contains("&lt;script&gt;"), "Script text encoded");

var clamped = EditorMarkup.Render("e2", "Html", height: 999999);
Require(clamped.Contains("min-height:1400px"), "Height clamped");

Console.WriteLine("Azunt.Editor smoke tests passed.");

static void Require(bool success, string test)
{
    if (!success) throw new Exception($"FAILED: {test}");
}

var pages = EditorMarkup.Render("report", "Body");
Require(pages.Contains("name=\"PaperSize\""), "Paper size form binding");
Require(pages.Contains("name=\"Orientation\""), "Orientation form binding");
Require(pages.Contains("name=\"Margin\""), "Margin form binding");

// Toolbars preserve their per-command contracts while adding source-owned SVGs.
var toolbar = EditorMarkup.Render("toolbar", "Content");
Require(toolbar.Contains("data-group-label=\"Text formatting\""), "Accessible toolbar group labels");
Require(toolbar.Contains("data-group-label=\"Document\""), "Document toolbar group");
Require(toolbar.Contains("azunt-editor-button-icon"), "Source-owned SVG button icons");
Require(toolbar.Contains("data-command=\"print\""), "Existing print command remains available");
Require(toolbar.Contains("data-command=\"image\""), "Existing image command remains available");

// Advanced editing controls are present in both MVC/Razor and Blazor common markup.
var advanced = EditorMarkup.Render("advanced", "Body");
foreach (var command in new[] { "table-merge-right", "table-split", "signature", "datetime", "templates" })
    Require(advanced.Contains($"data-command=\"{command}\""), $"Missing advanced command {command}");
foreach (var selector in new[] { "data-signature-canvas", "data-signature-insert", "data-template-list", "data-template-apply", "data-date-format", "tableBorderWidth" })
    Require(advanced.Contains(selector), $"Missing advanced control {selector}");

// Image alternative text uses the same editor markup for MVC, Razor Pages and Blazor.
Require(advanced.Contains("data-image-alt"), "Image alternative text field");
Require(advanced.Contains("maxlength=\"200\""), "Image alternative text length limit");

// Optional on-disk storage is not enabled unless a host supplies endpoints.
var imageFolder = Path.Combine(Path.GetTempPath(), "azunt-editor-test-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new Azunt.Editor.Storage.FileSystemEditorImageStore(imageFolder, "/editor/images");
    var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/sq8AAAAASUVORK5CYII=");
    await using var content = new MemoryStream(png);
    var result = await store.SaveAsync(content, "sample.png", "image/png");
    Require(store.IsManagedUrl(result.Url), "Managed image reference recognized");
    Require(!store.IsManagedUrl("//evil.example/editor/images/" + result.Url.Split('/').Last()), "Protocol-relative image rejected");
    Require(!store.IsManagedUrl("/editor/images/../secret"), "Traversal reference rejected");
    var restored = await store.ReadAsync(result.Url);
    Require(restored is not null && restored.Content.SequenceEqual(png) && restored.ContentType == "image/png",
        "Stored image can be read with identical bytes");
    Require(await store.ReadAsync("https://example.org/image.png") is null, "Remote image cannot be read");
    try
    {
        await store.SaveAsync(new MemoryStream([1, 2, 3]), "invalid.png", "image/png");
        throw new Exception("FAILED: Invalid image accepted");
    }
    catch (ArgumentException) { /* Expected. */ }
    Require(await store.DeleteUnreferencedAsync(result.Url), "Host can delete an unreferenced image");
    Require(await store.ReadAsync(result.Url) is null, "Deleted image cannot be read");
}
finally
{
    if (Directory.Exists(imageFolder)) Directory.Delete(imageFolder, recursive: true);
}
