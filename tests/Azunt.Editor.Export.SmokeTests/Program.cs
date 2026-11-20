using Azunt.Editor.Export.Models;
using Azunt.Editor.Export.Html;
using Azunt.Editor.Export.Services;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Text;

using var provider = new ServiceCollection()
    .AddAzuntEditorExport(options =>
        options.BrowserChannel = Environment.GetEnvironmentVariable("AZUNT_TEST_BROWSER_CHANNEL"))
    .BuildServiceProvider();
var exporter = provider.GetRequiredService<IEditorExportService>();
var samplePng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/sq8AAAAASUVORK5CYII=";
var html = "<h2>Export test</h2><p><strong>Bold</strong> and <a href=\"https://example.com\">a link</a></p>" +
    "<table><tr><th>Asset</th><th>Status</th></tr><tr><td>EGM-001</td><td>=1+1</td></tr></table>" +
    $"<p><img src=\"data:image/png;base64,{samplePng}\" width=\"80\" alt=\"Document figure\"></p>" +
    "<hr data-azunt-page-break=\"true\"><p>Second page</p>" +
    "<script>alert('bad')</script><p onclick=\"alert('bad')\">Safe</p>";

var word = await exporter.ExportAsync(html, EditorExportFormat.Docx);
Require(word.Content.Length > 1000, "DOCX has content");
using (var zip = new ZipArchive(new MemoryStream(word.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "word/document.xml");
    Require(xml.Contains("Export test"), "DOCX heading");
    Require(xml.Contains("w:type=\"page\""),
        "DOCX contains an actual page break");
    Require(xml.Contains("EGM-001"), "DOCX table text");
    Require(xml.Contains("cantSplit"), "DOCX prevents table row splitting");
    Require(xml.Contains("tblHeader"), "DOCX repeats semantic header rows");
    Require(!xml.Contains("<script") && !xml.Contains("onclick"), "DOCX dangerous markup excluded");
    Require(zip.Entries.Any(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)), "DOCX embeds PNG image");
    Require(xml.Contains("Document figure"), "DOCX retains image alternative text");
}

var excel = await exporter.ExportAsync(html, EditorExportFormat.Xlsx);
using (var zip = new ZipArchive(new MemoryStream(excel.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "xl/worksheets/sheet1.xml");
    Require(xml.Contains("EGM-001"), "XLSX table data");
    Require(xml.Contains("<x:cols") || xml.Contains("<cols"), "XLSX defines readable column widths");
    Require(xml.Contains("=1+1"), "XLSX preserves cell text");
    Require(!xml.Contains("<f>"), "XLSX avoids formula injection");
    Require(xml.Contains("<drawing"), "XLSX worksheet references an image drawing");
    Require(zip.Entries.Any(e => e.FullName.StartsWith("xl/media/", StringComparison.Ordinal)),
        "XLSX embeds an image binary");
    var drawing = Read(zip, "xl/drawings/drawing1.xml");
    Require(drawing.Contains("oneCellAnchor"), "XLSX image is anchored to a cell");
    Require(drawing.Contains("Document figure"), "XLSX retains image alternative text");
    Require(zip.GetEntry("xl/drawings/_rels/drawing1.xml.rels") is not null,
        "XLSX drawing relationships exist");
}

// Hosts can sanitize editor documents before saving them, not just during export.
var unsafeHtml = "<input type='hidden' value='x'><p onclick='alert(1)'>Keep me</p>" +
    "<script>bad()</script><img src='https://example.org/pixel.png' onerror='bad()'>" +
    "<a href='javascript:alert(1)'>Unsafe link</a><p>After input</p>";
var cleaned = EditorHtmlSanitizer.Sanitize(unsafeHtml);
Require(cleaned.Contains("Keep me") && cleaned.Contains("After input"),
    "Void unsupported tags must not discard subsequent editor content");
Require(!cleaned.Contains("onclick") && !cleaned.Contains("<script") && !cleaned.Contains("bad()"),
    "Script and event attributes must not survive sanitization");
Require(!cleaned.Contains("https://example.org/pixel") && !cleaned.Contains("javascript:"),
    "Disallowed images and link schemes must be removed");
Require(cleaned.Contains("Unsafe link"), "Link text must be preserved safely");
var cleanPageBreak = EditorHtmlSanitizer.Sanitize("<p>First</p><hr data-azunt-page-break='true'><p>Second</p>");
Require(cleanPageBreak.Contains("<hr data-azunt-page-break=\"true\">"),
    "Allowed editor page break must survive server sanitization");
Require(!EditorHtmlSanitizer.Sanitize("<p data-azunt-page-break='true'>Text</p>").Contains("data-azunt-page-break"),
    "Page break attribute is not allowed on ordinary paragraphs");
Require(EditorHtmlSanitizer.Sanitize("<a href='/reports/123'>Local</a>").Contains("href=\"/reports/123\""),
    "Safe root-relative links are retained after persistence");
Require(EditorHtmlSanitizer.Sanitize("<a href='https://example.org/'>External</a>").Contains("https://example.org/"),
    "Safe HTTPS links are retained after persistence");
Require(EditorHtmlSanitizer.Sanitize("<p title=\"ignored\">&lt;ok&gt;</p>").Contains("&lt;ok&gt;"),
    "Text entities remain encoded after HTML normalization");
// 0.7.1: code blocks retain only a validated language and encoded source.
var exportedCode = EditorHtmlSanitizer.Sanitize(
    "<pre data-language='javascript' onclick='bad()'><code>&lt;img src=x onerror=alert(1)&gt;</code></pre>");
Require(exportedCode.Contains("data-language=\"javascript\""), "Code language survives export sanitizer");
Require(exportedCode.Contains("&lt;img src=x onerror=alert(1)&gt;"), "Source code is exported as text, not markup");
Require(!exportedCode.Contains("onclick") && !exportedCode.Contains("<img"), "Code markup is inert during export");
Require(!EditorHtmlSanitizer.Sanitize("<pre data-language='invalid'><code>text</code></pre>")
    .Contains("data-language"), "Unsupported code languages are dropped on export");
var gif = "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==";
Require(EditorHtmlSanitizer.Sanitize($"<p><img src=\"data:image/gif;base64,{gif}\"></p>")
    .Contains("data:image/gif;base64,"), "Valid GIF image must survive sanitized HTML storage");
Require(!EditorHtmlSanitizer.Sanitize("<img src='data:image/gif;base64,AQIDBA=='>")
    .Contains("<img"), "Mismatched inline image payload must be removed");
try
{
    EditorHtmlSanitizer.Sanitize(string.Concat(Enumerable.Repeat("<div>", 130)));
    throw new Exception("FAILED: Deeply nested HTML was accepted");
}
catch (ArgumentException) { /* Expected: bounded HTML nesting. */ }

// Merged horizontal table cells and border settings survive server normalization
// and are emitted as editable Open XML rather than flattened text.
var resizedTableHtml = EditorHtmlSanitizer.Sanitize(
    "<table style='table-layout:fixed;position:absolute'><tr>" +
    "<td style='width:41.500%;position:fixed'>One</td>" +
    "<td style='width:58.500%'>Two</td></tr></table>");
Require(resizedTableHtml.Contains("table-layout:fixed"), "Export HTML retains fixed table layout");
Require(resizedTableHtml.Contains("width:41.500%") && resizedTableHtml.Contains("width:58.500%"),
    "Export HTML retains resized table column widths");
Require(!resizedTableHtml.Contains("position:"), "Export HTML drops unsupported CSS");
Require(!EditorHtmlSanitizer.Sanitize("<td style='width:500%'>Bad</td>").Contains("width:"),
    "Export HTML rejects invalid column widths");
var advancedHtml = EditorHtmlSanitizer.Sanitize(
    "<table style='border-color:#cc0000;border-width:2px'>" +
    "<tr><td colspan='2'>Merged heading</td><td>Third</td></tr>" +
    "<tr><td>One</td><td>Two</td><td>Three</td></tr></table>" +
    $"<p><img src='data:image/png;base64,{samplePng}' width='120' alt='Signature'></p>");
Require(advancedHtml.Contains("colspan=\"2\""), "Server keeps safe horizontal span");
Require(advancedHtml.Contains("border-color:#cc0000"), "Server keeps safe table border color");
Require(!EditorHtmlSanitizer.Sanitize("<table><tr><td colspan='999'>Bad span</td></tr></table>").Contains("colspan"),
    "Excessive table spans are removed");
var advancedDocx = await exporter.ExportAsync(advancedHtml, EditorExportFormat.Docx);
using (var zip = new ZipArchive(new MemoryStream(advancedDocx.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "word/document.xml");
    Require(xml.Contains("gridSpan") && xml.Contains("Merged heading"), "Word preserves horizontal cell merge");
    Require(zip.Entries.Any(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)),
        "Word embeds signature PNG");
}
var advancedXlsx = await exporter.ExportAsync(advancedHtml, EditorExportFormat.Xlsx);
using (var zip = new ZipArchive(new MemoryStream(advancedXlsx.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "xl/worksheets/sheet1.xml");
    Require(xml.Contains("mergeCell") && xml.Contains("A1:B1"), "Excel preserves horizontal cell merge");
    Require(zip.Entries.Any(e => e.FullName.StartsWith("xl/media/", StringComparison.Ordinal)),
        "Excel embeds signature PNG");
}

// 0.8.0: code keeps line boundaries, tabs, and literal text in Word and Excel.
// Presentation markers must never modify the saved editor source HTML.
var exportCodeHtml = EditorHtmlSanitizer.Sanitize(
    "<pre class='az-code-block' data-language='csharp'><code>" +
    "public class Example {&#10;\tstatic void Main() {&#10;" +
    "    Console.WriteLine(&quot;&lt;safe&gt;&quot;);&#10;" +
    "    // End of code&#10;}&#10;}</code></pre>" +
    "<table style='table-layout:fixed'><tr>" +
    "<th style='width:40.000%'>A</th><th style='width:60.000%'>B</th></tr>" +
    "<tr><td>One</td><td>=SUM(1,2)</td></tr></table>");
Require(exportCodeHtml.Contains("data-language=\"csharp\"") &&
        exportCodeHtml.Contains("&lt;safe&gt;"), "Code block remains inert and encoded");
var codeWord = await exporter.ExportAsync(exportCodeHtml, EditorExportFormat.Docx);
using (var zip = new ZipArchive(new MemoryStream(codeWord.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "word/document.xml");
    Require(xml.Contains("Consolas") && xml.Contains("F1F5F9"), "Word code monospace and background");
    Require(xml.Split("<w:br").Length >= 5 && xml.Contains("<w:tab"),
        "Word code line breaks and indentation render as native elements");
    Require(xml.Contains("&lt;safe&gt;") && !xml.Contains("<safe>"), "Word source code stays text");
    Require(xml.Contains("<w:tcW") && xml.Contains("w:type=\"pct\""),
        "Word preserves selected table column widths");
}
var codeExcel = await exporter.ExportAsync(exportCodeHtml, EditorExportFormat.Xlsx);
using (var zip = new ZipArchive(new MemoryStream(codeExcel.Content), ZipArchiveMode.Read))
{
    var sheet = Read(zip, "xl/worksheets/sheet1.xml");
    var styles = Read(zip, "xl/styles.xml");
    Require(sheet.Contains("public class Example") && sheet.Contains("// End of code") &&
        sheet.Contains("&lt;safe&gt;"), "Excel retains each code line as an editable text cell");
    Require(sheet.Contains("s=\"2\"") && sheet.Contains("s=\"1\""),
        "Excel applies code and table heading styles");
    Require(styles.Contains("Consolas") && styles.Contains("FFF1F5F9"),
        "Excel style palette includes code formatting");
    Require(sheet.Contains("=SUM(1,2)") && !sheet.Contains("<f>"),
        "Excel values resembling formulas remain inert inline text");
}

// 0.6.7: checked/unchecked native controls survive export sanitization
// but other input types and event/host-form attributes are discarded.
var safeControls = EditorHtmlSanitizer.Sanitize(
    "<p>Controls <input type='radio'> <input type='radio' checked> " +
    "<input type='checkbox'> <input type='checkbox' checked></p>" +
    "<input type='password' value='secret'><input type='checkbox' name='x' onchange='bad()'>");
Require(safeControls.Count(c => c == '<') == 7, "Four explicit options and one extra safe checkbox survive");
Require(safeControls.Contains("<input type=\"radio\">"), "Unchecked radio preserved");
Require(safeControls.Contains("<input type=\"radio\" checked>"), "Checked radio preserved");
Require(safeControls.Contains("<input type=\"checkbox\">"), "Unchecked checkbox preserved");
Require(safeControls.Contains("<input type=\"checkbox\" checked>"), "Checked checkbox preserved");
Require(!safeControls.Contains("password") && !safeControls.Contains("name=") &&
        !safeControls.Contains("onchange") && !safeControls.Contains("secret"), "Unapproved input attributes rejected");
var wordControls = await exporter.ExportAsync(safeControls, EditorExportFormat.Docx);
using (var zip = new ZipArchive(new MemoryStream(wordControls.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "word/document.xml");
    Require(xml.Contains("☐") && xml.Contains("☑") && xml.Contains("○") && xml.Contains("◉"),
        "DOCX uses native form-control glyphs");
}
var excelControls = await exporter.ExportAsync(safeControls, EditorExportFormat.Xlsx);
using (var zip = new ZipArchive(new MemoryStream(excelControls.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "xl/worksheets/sheet1.xml");
    Require(xml.Contains("☐") && xml.Contains("☑") && xml.Contains("○") && xml.Contains("◉"),
        "XLSX uses native form-control glyphs");
}

// Managed image references are opt-in. No remote URLs are ever fetched.
var storedImageUrl = "/editor/demo-images/" + new string('a', 32);
Require(!EditorHtmlSanitizer.Sanitize($"<img src='{storedImageUrl}'>").Contains("<img"),
    "Default sanitizer must reject non-inline image references");
Require(EditorHtmlSanitizer.Sanitize($"<img src='{storedImageUrl}'>",
    url => url == storedImageUrl).Contains(storedImageUrl),
    "Explicitly trusted stored reference can be preserved");
Require(!EditorHtmlSanitizer.Sanitize("<img src='//evil.example/img.png'>", _ => true).Contains("<img"),
    "Protocol-relative image rejected even with unsafe callback");
Require(!EditorHtmlSanitizer.Sanitize("<img src='/editor/demo-images/%2e%2e'>", _ => true).Contains("<img"),
    "Encoded traversal rejected");
using (var storedProvider = new ServiceCollection()
    .AddSingleton<IEditorImageResolver>(new TestImageResolver(storedImageUrl,
        Convert.FromBase64String(samplePng)))
    .AddAzuntEditorExport()
    .BuildServiceProvider())
{
    var storedExporter = storedProvider.GetRequiredService<IEditorExportService>();
    foreach (var format in new[] { EditorExportFormat.Docx, EditorExportFormat.Xlsx })
    {
        var file = await storedExporter.ExportAsync($"<p><img src='{storedImageUrl}' alt='Stored image'></p>", format);
        using var zip = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        Require(zip.Entries.Any(e => e.FullName.Contains("/media/", StringComparison.Ordinal)),
            $"{format} embeds a host-resolved image");
    }
}

// Final-save conversion: inline editing becomes short host-managed references.
var persistence = new EditorInlineImagePersistence();
var managedUrl = "/editor/demo-images/" + new string('b', 32);
var savedCalls = 0;
var savedImage = await persistence.PersistAsync(
    $"<p>Figure</p><p><img src='data:image/png;base64,{samplePng}' alt='One' width='120'></p>" +
    $"<p><img src='data:image/png;base64,{samplePng}' alt='Two'></p>",
    async (stream, fileName, contentType, token) =>
    {
        savedCalls++;
        Require(contentType == "image/png" && fileName.EndsWith(".png"), "Image save metadata");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, token);
        Require(buffer.ToArray().SequenceEqual(Convert.FromBase64String(samplePng)), "Image payload preserved");
        return managedUrl;
    }, url => url == managedUrl);
Require(savedCalls == 1 && savedImage.SavedImageCount == 1 && savedImage.ImageCount == 2,
    "Duplicate inline images are saved once per document");
Require(savedImage.Html.Contains(managedUrl) && !savedImage.Html.Contains("base64"),
    "Saved HTML uses short managed URLs");
Require(savedImage.Html.Contains("alt=\"One\"") && savedImage.Html.Contains("width=\"120\""),
    "Saved HTML keeps image attributes");
var repeated = await persistence.PersistAsync(savedImage.Html,
    (stream, fileName, contentType, token) => throw new Exception("Unexpected upload"),
    url => url == managedUrl);
Require(repeated.SavedImageCount == 0 && repeated.Html == savedImage.Html,
    "Saving a managed document again does not re-upload images");
var rejected = false;
try
{
    await persistence.PersistAsync("<img src='https://untrusted.example/pixel.png'>",
        (stream, name, contentType, token) => throw new Exception("Must not call storage"),
        _ => false);
}
catch (ArgumentException) { rejected = true; }
Require(rejected, "Remote image references are rejected before writes");
rejected = false;
try
{
    await persistence.PersistAsync($"<img src='data:image/png;base64,{samplePng}'>",
        (stream, name, contentType, token) => Task.FromResult("https://other.example/image"),
        url => url == managedUrl);
}
catch (InvalidOperationException) { rejected = true; }
Require(rejected, "Storage must return a validated root-relative URL");

if (Environment.GetEnvironmentVariable("AZUNT_TEST_PDF") == "1")
{
    var pdf = await exporter.ExportAsync(html, EditorExportFormat.Pdf,
        new EditorExportOptions { ShowPageNumbers = true });
    Require(Encoding.ASCII.GetString(pdf.Content, 0, 5) == "%PDF-", "PDF header");
}

Console.WriteLine("Azunt.Editor.Export smoke tests passed (DOCX, XLSX; PDF is opt-in).");

static string Read(ZipArchive zip, string entryName)
{
    var entry = zip.GetEntry(entryName) ?? throw new Exception($"Missing {entryName}");
    using var sr = new StreamReader(entry.Open());
    return sr.ReadToEnd();
}
static void Require(bool value, string description)
{
    if (!value) throw new Exception("FAILED: " + description);
}

internal sealed class TestImageResolver(string expectedUrl, byte[] png) : IEditorImageResolver
{
    public ValueTask<EditorResolvedImage?> ResolveAsync(string url, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<EditorResolvedImage?>(url == expectedUrl
            ? new EditorResolvedImage(png, "image/png") : null);
}
