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
