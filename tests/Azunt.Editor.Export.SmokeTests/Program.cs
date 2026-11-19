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
