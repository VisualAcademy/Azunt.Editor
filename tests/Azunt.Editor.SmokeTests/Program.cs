using Azunt.Editor.Models;
using Azunt.Editor.Rendering;

// Package-independent console tests: no xUnit/NUnit/external test framework.
var safe = EditorMarkup.Render("edit-one", "Description", "<p>Hello</p>");
Require(safe.Contains("data-azunt-editor=\"true\""), "Root marker");
Require(safe.Contains("name=\"Description\""), "Field name");
Require(safe.Contains("&lt;p&gt;Hello&lt;/p&gt;"), "Encoded initial HTML");
Require(safe.Contains("data-azunt-images=\"true\""), "Multiple image input");
Require(safe.Contains("multiple"), "Multiple file selection");

// Editable toolbar controls skip native Tab order even before JS mounts.
var toolbarMarkup = safe.Split("class=\"az-azunt-editor-toolbar\"", 2)[1]
    .Split("data-code-panel", 2)[0];
var toolbarControls = System.Text.RegularExpressions.Regex.Matches(toolbarMarkup, "<(button|select|input)\\b[^>]*>");
Require(toolbarControls.Count > 0, "Toolbar controls rendered");
foreach (System.Text.RegularExpressions.Match control in toolbarControls)
    Require(control.Value.Contains("tabindex=\"-1\""), "Editable toolbar control skips Tab");
Require(safe.Contains("aria-keyshortcuts=\"Alt+F10\""), "Canvas advertises keyboard toolbar shortcut");


// Native rendering is opt-in; existing overloads generate unchanged root markup.
Require(!safe.Contains("data-content-rendering="), "Legacy default is Standard");
foreach (var renderingMode in new[] { EditorMode.Edit, EditorMode.View })
{
    var nativeMarkup = EditorMarkup.Render("native", "Html", "<p>Native</p>", 320,
        false, null, "Compact", renderingMode, true, true, EditorContentRendering.BrowserDefault);
    Require(nativeMarkup.Contains("data-content-rendering=\"browser-default\""), "Native rendering marker");
    Require(nativeMarkup.Contains(renderingMode == EditorMode.View ? "<p>Native</p>" : "&lt;p&gt;Native&lt;/p&gt;"), "HTML preserved by native mode");
}

Require(!safe.Contains("data-select=\"documentStyle\""), "Style selector is opt-in");
foreach (var (style, marker) in new[] {
    (EditorContentRendering.CompactReport, "compact-report"),
    (EditorContentRendering.BusinessDocument, "business-document"),
    (EditorContentRendering.Minimal, "minimal") })
{
    var styleMarkup = EditorMarkup.Render("styled", "Html", "<p>Report</p>", 320,
        false, null, "Compact", EditorMode.Edit, true, true, style, true);
    Require(styleMarkup.Contains($"data-content-rendering=\"{marker}\""), "Style marker");
    Require(styleMarkup.Contains("data-select=\"documentStyle\""), "Opt-in selector");
    Require(styleMarkup.Contains($"value=\"{style}\" selected"), "Initial style selected");
    Require(styleMarkup.Contains("&lt;p&gt;Report&lt;/p&gt;"), "Style does not mutate HTML");
}

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
// 0.7.0: restricted cell percentage widths survive SSR, arbitrary CSS is rejected.
var resizedViewer = EditorViewerSanitizer.Sanitize(
    "<table style='table-layout:fixed;position:fixed'><tr>" +
    "<td style='width:63.333%;position:absolute'>Left</td>" +
    "<td style='width:36.667%'>Right</td></tr></table>");
Require(resizedViewer.Contains("table-layout:fixed"), "Table layout survives View sanitization");
Require(resizedViewer.Contains("width:63.333%") && resizedViewer.Contains("width:36.667%"),
    "Column percentage widths survive View sanitization");
Require(!resizedViewer.Contains("position:"), "Unsupported table CSS is removed");
Require(!EditorViewerSanitizer.Sanitize("<td style='width:999%;width:calc(100%)'>Bad</td>").Contains("width:"),
    "Disallowed percentage widths are rejected in View");
var rgbViewer = Azunt.Editor.Rendering.EditorViewerSanitizer.Sanitize(
    "<p style=\"color:rgb(18, 52, 86);background-color:rgba(255, 220, 10, 1)\">Colors</p>");
Require(rgbViewer.Contains("#123456") && rgbViewer.Contains("#ffdc0a"), "Browser RGB colors survive server rendering");

// 0.7.0: pasted heading/footer/strike markup has the same browser/Viewer
// allowlist; remote active content and source-page metadata are not rendered.
var pastedViewer = EditorViewerSanitizer.Sanitize(
    "<head><title>Hidden metadata</title><style>body{color:red}</style></head>" +
    "<h1>Title</h1><h4>Subtitle</h4><p><del>Removed</del><strike>Old</strike></p>" +
    "<table><tfoot><tr><td>Summary</td></tr></tfoot></table>");
Require(pastedViewer.Contains("<h1>Title</h1>"), "Pasted H1 survives Viewer");
Require(pastedViewer.Contains("<h4>Subtitle</h4>"), "Pasted H4 survives Viewer");
Require(pastedViewer.Contains("<del>Removed</del>"), "Pasted deletion formatting survives Viewer");
Require(pastedViewer.Contains("<tfoot>"), "Pasted table footer survives Viewer");
Require(!pastedViewer.Contains("Hidden metadata") && !pastedViewer.Contains("body{color:red}"),
    "Source page head is not inserted into View");

// 0.7.1: code blocks remain inert in SSR, with a validated language and encoded source.
var codeToolbar = EditorMarkup.Render("code-ui", "Html");
Require(codeToolbar.Contains("data-quick-command=\"code-block\""), "Compact code action");
Require(codeToolbar.Contains("data-command=\"code-block\""), "Classic code action");
Require(codeToolbar.Contains("data-azunt-menu=\"codepicker\""), "Code language picker available");
Require(codeToolbar.Contains("data-code-text") && codeToolbar.Contains("data-code-apply"), "Code editor textarea and Apply action");
var codeViewer = EditorViewerSanitizer.Sanitize(
    "<pre data-language='html' onclick='alert(1)'><code>&lt;script&gt;alert(1)&lt;/script&gt;</code></pre>");
Require(codeViewer.Contains("class=\"az-code-block\" data-language=\"html\""), "Safe code language survives View");
Require(codeViewer.Contains("&lt;script&gt;alert(1)&lt;/script&gt;"), "Code is always encoded as text");
Require(!codeViewer.Contains("onclick") && !codeViewer.Contains("<script>"), "View code never executes as HTML");
Require(!EditorViewerSanitizer.Sanitize("<pre data-language='unknown'><code>x</code></pre>")
    .Contains("data-language"), "Unknown code languages are not persisted");

// 0.6.8: both toolbar modes expose the table split control and its grid picker.
var tableToolbar = EditorMarkup.Render("table-palette", "Html");
Require(tableToolbar.Contains("data-table-move-handle"), "Table movement grip is host-rendered");
Require(tableToolbar.Contains("data-table-column-handle"), "Column resize grip is host-rendered");
Require(tableToolbar.Contains("data-table-drop-marker"), "Table drop indicator is host-rendered");
Require(tableToolbar.Contains("data-quick-command=\"table\""), "Quick default 2x2 table insertion retained");
Require(tableToolbar.Contains("data-command=\"table\""), "Classic default 2x2 table insertion retained");
Require(tableToolbar.Split("data-azunt-menu=\"tablepicker\"").Length == 3, "Table grid dropdown on both toolbars");

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
var notes = EditorMarkup.Render("notes", "Description", "<p>Memo</p>", 320, false, null, "Compact", EditorMode.Edit, showMenuBar: false);
Require(compact.Contains("data-show-menu-bar=\"true\""), "Menu bar remains visible by default");
Require(notes.Contains("data-show-menu-bar=\"false\""), "Note editor hides the menu bar");
Require(notes.Contains("data-show-toolbar-group-labels=\"true\""), "Group labels visible by default");
var labelFree = EditorMarkup.Render("label-free", "Description", "<p>Memo</p>", 320, false, null, "Compact", EditorMode.Edit, false, false);
Require(labelFree.Contains("data-show-toolbar-group-labels=\"false\""), "Group labels can be hidden");
Require(notes.Contains("data-azunt-menu=\"more\""), "Note editor retains More commands");
Require(notes.Contains("name=\"Description\""), "Note editor retains form field binding");
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
