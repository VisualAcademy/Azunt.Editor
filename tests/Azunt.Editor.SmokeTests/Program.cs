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

// 0.6.6 View is safe and visible in the server response before JavaScript runs.
var initialViewer = EditorMarkup.Render("view", "Html", "<h2>Stored heading</h2><p>Formatted <strong>text</strong></p>",
    mode: Azunt.Editor.Models.EditorMode.View);
Require(initialViewer.Contains("data-editor-mode=\"view\""), "View marker");
Require(initialViewer.Contains("<h2>Stored heading</h2>"), "View HTML rendered on server");
Require(initialViewer.Contains("<strong>text</strong>"), "View bold retained");
Require(initialViewer.Contains("name=\"Html\" disabled"), "View value cannot be submitted");
Require(initialViewer.Contains("role=\"document\""), "View accessibility role");
var dangerousViewer = EditorMarkup.Render("safe", "Html",
    "<p onclick=\"alert(1)\">Safe</p><script>alert('evil')</script>" +
    "<a href=\"javascript:alert(1)\">link</a><img src=\"javascript:alert(1)\" onerror=\"alert(2)\">" +
    "<span style=\"color:red;position:fixed\">inline</span>",
    mode: Azunt.Editor.Models.EditorMode.View);
var displayedViewerHtml = dangerousViewer.Split("class=\"az-azunt-editor-canvas\"", 2)[1].Split("</div>", 2)[0];
Require(displayedViewerHtml.Contains("<p>Safe</p>"), "Viewer removes event handlers");
Require(!displayedViewerHtml.Contains("<script", StringComparison.OrdinalIgnoreCase), "Viewer removes script tags");
Require(!displayedViewerHtml.Contains("javascript:", StringComparison.OrdinalIgnoreCase), "Viewer rejects dangerous URLs");
Require(!displayedViewerHtml.Contains("<img", StringComparison.OrdinalIgnoreCase), "Viewer rejects dangerous images");
Require(!displayedViewerHtml.Contains("position:fixed", StringComparison.OrdinalIgnoreCase), "Viewer rejects unsupported CSS");
var inlineViewer = Azunt.Editor.Rendering.EditorViewerSanitizer.Sanitize(
    "<pre><code>Console.WriteLine(&quot;Hello&quot;);</code></pre><table><tr><td colspan=\"2\">Cell</td></tr></table>");
Require(inlineViewer.Contains("<pre><code>"), "Code blocks survive viewer sanitizer");
Require(inlineViewer.Contains("colspan=\"2\""), "Table colspan survives viewer sanitizer");
var rgbViewer = Azunt.Editor.Rendering.EditorViewerSanitizer.Sanitize(
    "<p style=\"color:rgb(18, 52, 86);background-color:rgba(255, 220, 10, 1)\">Colors</p>");
Require(rgbViewer.Contains("#123456") && rgbViewer.Contains("#ffdc0a"), "Browser RGB colors survive server rendering");

// 0.6.7: both toolbar modes expose the split form-control insertion button.
var formToolbar = EditorMarkup.Render("forms", "Html");
Require(formToolbar.Contains("data-quick-command=\"form-checkbox-checked\""), "Quick checked checkbox insertion");
Require(formToolbar.Contains("data-command=\"form-checkbox-checked\""), "Classic checked checkbox insertion");
Require(formToolbar.Contains("data-azunt-menu=\"controls\""), "Split dropdown in both modes");
var controlsView = EditorMarkup.Render("forms-view", "Html",
    "<p><input type='checkbox' checked onclick='bad()'> Checkbox <input type='radio'> Radio</p>",
    mode: Azunt.Editor.Models.EditorMode.View);
var controlsCanvas = controlsView.Split("class=\"az-azunt-editor-canvas\"", 2)[1].Split("</div>", 2)[0];
Require(controlsCanvas.Contains("<input type=\"checkbox\" checked disabled>"), "Checked checkbox SSR is inert");
Require(controlsCanvas.Contains("<input type=\"radio\" disabled>"), "Radio SSR is inert");
Require(!controlsCanvas.Contains("onclick"), "Form input event handlers are stripped");
var unsafeControls = Azunt.Editor.Rendering.EditorViewerSanitizer.Sanitize(
    "<input type='password' value='secret'><input type='text' name='x'>" +
    "<input type='checkbox' name='answer' value='yes' autofocus checked>");
Require(unsafeControls == "<input type=\"checkbox\" checked disabled>", "Only narrow form attributes survive SSR");

var clamped = EditorMarkup.Render("e2", "Html", height: 999999);
Require(clamped.Contains("min-height:1400px"), "Height clamped");

// 0.6.0 API remains compatible with both toolbar modes and existing form field names.
var compact = EditorMarkup.Render("compact", "Html", "<p>Text</p>", toolbarMode: "Compact");
var classic = EditorMarkup.Render("classic", "Html", "<p>Text</p>", toolbarMode: "Classic");
Require(compact.Contains("data-toolbar-mode=\"compact\""), "Compact toolbar mode");
Require(classic.Contains("data-toolbar-mode=\"classic\""), "Classic toolbar mode");
Require(compact.Contains("name=\"Html\""), "MVC/Razor form binding remains stable");
Require(classic.Contains("data-azunt-source"), "Classic source edit control remains available");
var customCommand = new Azunt.Editor.Models.EditorCustomCommand
{
    Id = "saveDraft", Label = "Save draft", Group = "File", Placement = "Toolbar",
    Icon = "S", RequiresEdit = true
};
Require(customCommand.RequiresEdit && customCommand.Icon == "S", "Custom command options");
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
Require(toolbar.Contains("az-azunt-editor-button-icon"), "Source-owned SVG button icons");
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

// MVC/Razor Pages markup remains automatically mountable.
if (safe.Contains("data-azunt-mount=\"manual\"")) throw new Exception("Non-Blazor editor became manually mounted");
