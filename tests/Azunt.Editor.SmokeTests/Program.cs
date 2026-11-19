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
