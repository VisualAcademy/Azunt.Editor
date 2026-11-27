using Azunt.Editor.Export.Models;
using Azunt.Editor.Export.Html;
using Azunt.Editor.Export.Import;
using Azunt.Editor.Export.Services;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

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
    Require(zip.Entries.Any(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal) || e.FullName.StartsWith("media/", StringComparison.Ordinal)), "DOCX embeds PNG image");
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
Require(EditorHtmlSanitizer.Sanitize("<table style='width:100%'><tr><td>New</td></tr></table>").Contains("width:100%"),
    "New full-width tables retain their width");
Require(EditorHtmlSanitizer.Sanitize("<table style='width:62.125%'><tr><td>Resized</td></tr></table>").Contains("width:62.125%"),
    "Resized table width survives sanitization");
foreach (var invalidOuterWidth in new[] { "0%", "100.1%", "-10%", "80px", "calc(50%)" })
    Require(!EditorHtmlSanitizer.Sanitize($"<table style='width:{invalidOuterWidth}'><tr><td>Bad</td></tr></table>").Contains("width:"),
        "Invalid outer width is rejected");
Require(!EditorHtmlSanitizer.Sanitize("<table style='width:999%;position:fixed'><tr><td>Bad</td></tr></table>").Contains("width:"),
    "Invalid table widths remain rejected");
var rowResizeExportHtml = EditorHtmlSanitizer.Sanitize(
    "<table><tr style='height:80px;position:fixed'><td>Row</td></tr>" +
    "<tr style='height:9999px'><td>Invalid</td></tr></table>");
Require(rowResizeExportHtml.Contains("height:80px"), "Export HTML retains bounded row heights");
Require(!rowResizeExportHtml.Contains("9999px") && !rowResizeExportHtml.Contains("position:"),
    "Export HTML rejects excessive row heights and unrelated CSS");
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
    Require(zip.Entries.Any(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal) || e.FullName.StartsWith("media/", StringComparison.Ordinal)),
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

// AE02-03: Analytics-like edited data, typed columns opt-in, and stable row positions.
var reportTable = "<table><thead><tr><th>#</th><th>Open</th><th>Qty</th><th>Notes</th></tr></thead><tbody>" +
    "<tr><td>000123</td><td>11/18/2026 2:20:00 PM</td><td>12.5</td><td>Line one<br>Line two</td></tr>" +
    "<tr><td>123456789012345678</td><td>-</td><td>001</td><td>=SUM(1,2)</td></tr>" +
    "<tr><td colspan='2'>Merged</td><td>2</td><td><table><tr><td>Nested</td></tr></table></td></tr></tbody></table>";
var reportExcel = await exporter.ExportAsync(reportTable, EditorExportFormat.Xlsx, new EditorExportOptions
{
    SpreadsheetDateColumns = new[] { "Open" }, SpreadsheetNumberColumns = new[] { "Qty" }, SpreadsheetCulture = "en-US"
});
using (var zip = new ZipArchive(new MemoryStream(reportExcel.Content), ZipArchiveMode.Read))
{
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var sheet = XDocument.Parse(Read(zip, "xl/worksheets/sheet1.xml"));
    XElement Cell(string reference) => sheet.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == reference);
    string? Type(string reference) => (string?)Cell(reference).Attribute("t");
    Require(Type("A2") == "inlineStr" && Cell("A2").Value == "000123", "Excel preserves leading-zero identifiers");
    Require(Type("A3") == "inlineStr" && Cell("A3").Value == "123456789012345678", "Excel preserves long IDs");
    Require(Type("B2") == "n" && (string?)Cell("B2").Attribute("s") == "4" &&
        Math.Abs(double.Parse(Cell("B2").Value, System.Globalization.CultureInfo.InvariantCulture) -
            new DateTime(2026, 11, 18, 14, 20, 0).ToOADate()) < 0.000001, "Excel date is a native serial with a date style");
    Require(Type("C2") == "n" && Cell("C2").Value == "12.5", "Excel quantity is numeric");
    Require(Type("C3") == "inlineStr" && Cell("C3").Value == "001", "Excel keeps ambiguous numeric IDs as text");
    Require(Type("B3") == "inlineStr" && Cell("B3").Value == "-", "Excel preserves missing dates");
    Require(Cell("D2").Value == "Line one\nLine two", "Excel preserves explicit line breaks");
    Require(Type("D3") == "inlineStr" && !sheet.Descendants(ns + "f").Any(), "Excel never emits formulas from HTML");
    Require(sheet.Descendants(ns + "mergeCell").Any(c => (string?)c.Attribute("ref") == "A4:B4"), "Excel preserves horizontal merge");
    Require(Cell("D4").Value.Trim() == "Nested" && sheet.Descendants(ns + "row").Count() == 5,
        "Nested table is not duplicated as extra data rows");
    Require(Read(zip, "xl/styles.xml").Contains("yyyy-mm-dd hh:mm:ss"), "Excel has a date format");
}
var defaultReportExcel = await exporter.ExportAsync(reportTable, EditorExportFormat.Xlsx);
using (var zip = new ZipArchive(new MemoryStream(defaultReportExcel.Content), ZipArchiveMode.Read))
{
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var sheet = XDocument.Parse(Read(zip, "xl/worksheets/sheet1.xml"));
    Require(sheet.Descendants(ns + "c").Where(c => (string?)c.Attribute("r") is "B2" or "C2")
        .All(c => (string?)c.Attribute("t") == "inlineStr"), "Default exports retain literal values without opt-in");
}
var tableImageExcel = await exporter.ExportAsync($"<table><tr><td><img src='data:image/png;base64,{samplePng}' width='80'></td></tr><tr><td>Next row</td></tr></table>", EditorExportFormat.Xlsx);
using (var zip = new ZipArchive(new MemoryStream(tableImageExcel.Content), ZipArchiveMode.Read))
{
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var sheet = XDocument.Parse(Read(zip, "xl/worksheets/sheet1.xml"));
    Require(sheet.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "A2").Value == "Next row", "Table image does not insert extra rows");
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

// 0.8.1: verify actual Open XML package behavior, not just HTML/CSS source.
var relativeLinkWord = await exporter.ExportAsync(
    "<p><a href='/reports/123'>Relative report</a> and " +
    "<a href='#section'>Section marker</a> and " +
    "<a href='https://example.org/report'>External report</a></p>",
    EditorExportFormat.Docx);
using (var zip = new ZipArchive(new MemoryStream(relativeLinkWord.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "word/document.xml");
    var relations = Read(zip, "word/_rels/document.xml.rels");
    Require(xml.Contains("Relative report") && xml.Contains("Section marker"),
        "Word relative and fragment links remain readable text");
    Require(relations.Contains("https://example.org/report") &&
        !relations.Contains("/reports/123") && !relations.Contains("#section"),
        "Only absolute links generate external Word relationships");
}
var coloredCodeWord = await exporter.ExportAsync(
    "<pre data-language='csharp'><code>public class Demo {&#10; " +
    "// comment&#10; string name = &quot;safe&quot;;&#10;}</code></pre>",
    EditorExportFormat.Docx);
using (var zip = new ZipArchive(new MemoryStream(coloredCodeWord.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "word/document.xml");
    Require(xml.Contains("7C3AED") && xml.Contains("64748B") && xml.Contains("167347"),
        "Word contains different keyword, comment and string colors");
    Require(xml.Contains("public") && xml.Contains("// comment") && xml.Contains("safe") &&
        xml.Contains("<w:br"), "Word retains unchanged code and line boundaries");
}
var naturalImageWord = await exporter.ExportAsync(
    $"<p><img src='data:image/png;base64,{samplePng}' alt='Pixel'></p>",
    EditorExportFormat.Docx);
using (var zip = new ZipArchive(new MemoryStream(naturalImageWord.Content), ZipArchiveMode.Read))
{
    var xml = Read(zip, "word/document.xml");
    Require(xml.Contains("cx=\"9525\"") && xml.Contains("cy=\"9525\""),
        "Word keeps a 1x1 PNG square without forcing its height to 20px");
}
// Place a supplementary Unicode code point directly across the 32,767 UTF-16
// boundary to ensure a code line is split *before* a surrogate pair.
var unicodeCodeLine = new string('x', 32766) + "😀END";
var unicodeExcel = await exporter.ExportAsync(
    "<pre data-language='csharp'><code>" + unicodeCodeLine + "</code></pre>",
    EditorExportFormat.Xlsx);
using (var zip = new ZipArchive(new MemoryStream(unicodeExcel.Content), ZipArchiveMode.Read))
{
    var sheet = Read(zip, "xl/worksheets/sheet1.xml");
    using var reader = System.Xml.XmlReader.Create(new StringReader(sheet));
    while (reader.Read()) { }
    Require(sheet.Contains("😀END") && sheet.Contains(new string('x', 100)),
        "Excel retains a complete Unicode code point across long-cell boundaries");
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

// PDF options must not alter the DOCX/XLSX path, even when PDF-specific values
// would be invalid for a PDF render. Validation is intentionally PDF-only.
var pdfOnlyOptions = new EditorExportOptions
{
    PdfLayout = EditorPdfLayout.Compact, PdfFontSize = 7, PdfTableFontSize = 37,
    PromotePdfTableHeaders = true
};
foreach (var format in new[] { EditorExportFormat.Docx, EditorExportFormat.Xlsx })
{
    var unaffected = await exporter.ExportAsync(html, format, pdfOnlyOptions);
    using var archive = new ZipArchive(new MemoryStream(unaffected.Content), ZipArchiveMode.Read);
    Require(Read(archive, format == EditorExportFormat.Docx ? "word/document.xml" : "xl/worksheets/sheet1.xml")
        .Contains("EGM-001"), $"{format} ignores PDF options");
}
foreach (var invalid in new[] {
    new EditorExportOptions { PdfFontSize = 7 },
    new EditorExportOptions { PdfTableFontSize = 37 },
    new EditorExportOptions { PdfTableCellPadding = -1 },
    new EditorExportOptions { PdfTableCellPadding = 25 },
    new EditorExportOptions { PdfLayout = (EditorPdfLayout)99 } })
{
    var invalidRejected = false;
    try { await exporter.ExportAsync(html, EditorExportFormat.Pdf, invalid); }
    catch (ArgumentOutOfRangeException) { invalidRejected = true; }
    Require(invalidRejected, "Invalid PDF settings rejected before browser launch");
}
Require(new EditorExportOptions().PdfLayout == EditorPdfLayout.Standard &&
    new EditorExportOptions().PdfFontSize is null && !new EditorExportOptions().PromotePdfTableHeaders,
    "PDF defaults remain backward compatible");

// 0.8.5: real DOCX package checks, including schema validation, grid widths,
// explicit formatting, paragraph boundaries, opt-in defaults and format isolation.
var wordReport = "<p>Body text</p><p><span style='font-size:20px;color:#245ca6'><b><u>Explicit</u></b></span></p>" +
    "<table><tr><th colspan='3'>Title</th></tr><tr><th style='width:20%'>Number</th>" +
    "<th style='width:30%'>Department</th><th>Summary</th></tr>" +
    "<tr><td>000123</td><td><p>First</p><p>Second</p></td><td><table><tr><td>Nested</td></tr></table></td></tr>" +
    "<tr><th>Not a repeating header</th><th>X</th><th>Y</th></tr></table>" +
    "<p style='margin-left:20px'>Indented</p>" +
    $"<p><img src='data:image/png;base64,{samplePng}' width='80' alt='Signature'></p>";
var wordOutputDirectory = Environment.GetEnvironmentVariable("AZUNT_TEST_WORD_OUTPUT");
foreach (var layout in Enum.GetValues<EditorWordLayout>())
{
    var result = await exporter.ExportAsync(wordReport, EditorExportFormat.Docx, new EditorExportOptions
    { WordLayout = layout, PaperSize = "Letter" });
    using var archive = new ZipArchive(new MemoryStream(result.Content), ZipArchiveMode.Read);
    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    var document = XDocument.Parse(Read(archive, "word/document.xml"));
    var table = document.Descendants(w + "tbl").Single();
    var grid = table.Element(w + "tblGrid")!.Elements(w + "gridCol")
        .Select(c => (int)c.Attribute(w + "w")!).ToArray();
    Require(grid.Length == 3 && grid.Sum() == 12240 - 1814 && grid.All(c => c > 0), "Word grid fits printable page");
    Require(grid[0] < grid[1] && grid[1] < grid[2], "Word grid respects partial width hints");
    var rows = table.Elements(w + "tr").ToArray();
    Require(rows.Length == 4 && rows[0].Descendants(w + "gridSpan").Single().Attribute(w + "val")!.Value == "3",
        "Merged heading and nested table do not alter outer grid");
    Require(rows[0].Descendants(w + "tblHeader").Any() && rows[1].Descendants(w + "tblHeader").Any() &&
        !rows[3].Descendants(w + "tblHeader").Any(), "Only initial headers repeat");
    var paragraphs = rows[2].Elements(w + "tc").ElementAt(1).Elements(w + "p").Select(p => p.Value).ToArray();
    Require(paragraphs.SequenceEqual(new[] { "First", "Second" }), "Word retains separate cell paragraphs");
    Require(document.Descendants(w + "t").Count(t => t.Value == "Nested") == 1 &&
        document.Descendants(w + "t").Any(t => t.Value == "000123"), "Nested text once, identifier unchanged");
    var explicitRun = document.Descendants(w + "r").Single(run => run.Element(w + "t")?.Value == "Explicit");
    Require(explicitRun.Descendants(w + "sz").Single().Attribute(w + "val")!.Value == "30", "Explicit font size wins");
    var tableRun = rows[2].Descendants(w + "r").First();
    if (layout == EditorWordLayout.Standard)
    {
        Require(!tableRun.Descendants(w + "sz").Any() && !table.Descendants(w + "tblCellMar").Any(),
            "Standard preserves implicit font and padding");
    }
    else
    {
        var expected = layout == EditorWordLayout.Compact ? 18 : 24;
        Require((int)tableRun.Descendants(w + "sz").Single().Attribute(w + "val")! == expected, "Word table profile font");
        Require(table.Descendants(w + "tblCellMar").Any(), "Word table padding emitted");
    }
    using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(result.Content), false);
    var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package).ToList();
    Require(errors.Count == 0, "DOCX schema validation: " + string.Join("; ", errors.Select(e => e.Description)));
    if (!string.IsNullOrWhiteSpace(wordOutputDirectory))
    {
        Directory.CreateDirectory(wordOutputDirectory);
        await File.WriteAllBytesAsync(Path.Combine(wordOutputDirectory, $"{layout}.docx"), result.Content);
    }
}
// 0.8.12: an HTML caret placeholder must not add an extra Word line.
foreach (var placeholderLayout in Enum.GetValues<EditorWordLayout>())
{
    var placeholderWord = await exporter.ExportAsync(
        "<p><br></p><table><tr><td>a</td><td><br></td><td><p><br></p></td>" +
        "<td> \n<br> </td><td>x<br>y</td><td><br><br></td>" +
        "<td><p>First</p><p><br></p><p>Last</p></td></tr></table>",
        EditorExportFormat.Docx, new EditorExportOptions { WordLayout = placeholderLayout });
    using var placeholderArchive = new ZipArchive(new MemoryStream(placeholderWord.Content), ZipArchiveMode.Read);
    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    var placeholderDoc = XDocument.Parse(Read(placeholderArchive, "word/document.xml"));
    var cells = placeholderDoc.Descendants(w + "tc").ToArray();
    Require(cells.Length == 7, "Placeholder test cell count");
    foreach (var cellIndex in new[] { 1, 2, 3 })
    {
        Require(cells[cellIndex].Elements(w + "p").Count() == 1 && !cells[cellIndex].Descendants(w + "br").Any(),
            "Empty HTML cell produces exactly one Word paragraph without a break");
        if (placeholderLayout == EditorWordLayout.Compact)
            Require(cells[cellIndex].Descendants(w + "pPr").Single().Element(w + "rPr")?.Element(w + "sz")?.Attribute(w + "val")?.Value == "18",
                "Empty compact cell uses 9pt paragraph mark");
    }
    Require(cells[4].Descendants(w + "br").Count() == 1 && cells[4].Descendants(w + "t").Select(t => t.Value).SequenceEqual(new[] { "x", "y" }),
        "Intentional in-cell line break retained");
    Require(cells[5].Descendants(w + "br").Count() == 2, "Multiple explicit breaks retained");
    Require(cells[6].Elements(w + "p").Count() == 3 && !cells[6].Descendants(w + "br").Any(), "Intentional blank paragraph retained without extra break");
    Require(!placeholderDoc.Descendants(w + "body").Single().Elements(w + "p").First().Descendants(w + "br").Any(), "Body caret placeholder becomes one blank paragraph");
    using var placeholderPackage = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(placeholderWord.Content), false);
    var placeholderErrors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(placeholderPackage).ToList();
    Require(placeholderErrors.Count == 0, "Placeholder DOCX schema validation: " + string.Join("; ", placeholderErrors.Select(e => e.Description)));
}
// 0.8.13: match-document profiles are optional and independent of legacy export defaults.
var matchedHtml = "<h2>Heading</h2><p>Body</p><table style='width:65%;table-layout:fixed'>" +
    "<tr><th style='width:70%'>Number</th><th style='width:30%'>Status</th></tr>" +
    "<tr style='height:40px'><td><br></td><td><span style='font-size:20px'>Explicit</span></td></tr></table>";
foreach (var presetId in new[] { "Standard", "BrowserDefault", "CompactReport", "BusinessDocument", "Minimal", "custom" })
{
    var matchedStyle = presetId == "custom" ? new EditorExportDocumentStyle
        { FontFamily = "Arial", FontSize = 12, TableFontSize = 11, CellPaddingVertical = 2, CellPaddingHorizontal = 4 } :
        EditorExportDocumentStyle.FromPreset(presetId);
    var matchedResult = await exporter.ExportAsync(matchedHtml, EditorExportFormat.Docx,
        new EditorExportOptions { DocumentStyle = matchedStyle, PaperSize = "Letter" });
    using var matchedZip = new ZipArchive(new MemoryStream(matchedResult.Content), ZipArchiveMode.Read);
    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    var matchedDocument = XDocument.Parse(Read(matchedZip, "word/document.xml"));
    var matchedStyles = XDocument.Parse(Read(matchedZip, "word/styles.xml"));
    var matchedTable = matchedDocument.Descendants(w + "tbl").Single();
    Require((int)matchedTable.Descendants(w + "tblW").Single().Attribute(w + "w")! == 3250, "Matched Word table width 65 percent");
    var matchedGrid = matchedTable.Descendants(w + "gridCol").Select(c => (int)c.Attribute(w + "w")!).ToArray();
    Require(matchedGrid.Sum() == (int)Math.Round((12240 - 1814) * .65) &&
        Math.Abs((double)matchedGrid[0] / matchedGrid.Sum() - .7) < .001, "Matched table grid preserves 70:30 proportions");
    var matchedHeight = matchedTable.Descendants(w + "trHeight").Single();
    Require((int)matchedHeight.Attribute(w + "val")! == 600 && (string?)matchedHeight.Attribute(w + "hRule") == "atLeast", "Row height is a minimum, not clipping text");
    var margins = matchedTable.Descendants(w + "tblCellMar").Single();
    Require((int)margins.Element(w + "top")!.Attribute(w + "w")! == matchedStyle.CellPaddingVertical * 15 &&
        (int)margins.Element(w + "left")!.Attribute(w + "w")! == matchedStyle.CellPaddingHorizontal * 15, "Separate vertical and horizontal cell padding");
    var emptyCell = matchedTable.Descendants(w + "tc").ElementAt(2);
    var expectedHalfPoints = (int)Math.Round(matchedStyle.TableFontSize * 1.5, MidpointRounding.AwayFromZero);
    Require(!emptyCell.Descendants(w + "br").Any() && (int)emptyCell.Descendants(w + "sz").Single().Attribute(w + "val")! == expectedHalfPoints, "Empty cell retains one paragraph at matched font size");
    var explicitMatchedRun = matchedDocument.Descendants(w + "r").Single(r => r.Element(w + "t")?.Value == "Explicit");
    Require((int)explicitMatchedRun.Descendants(w + "sz").Single().Attribute(w + "val")! == 30, "Inline font size wins over matched profile");
    Require((int)matchedStyles.Descendants(w + "sz").Single().Attribute(w + "val")! == (int)Math.Round(matchedStyle.FontSize * 1.5, MidpointRounding.AwayFromZero), "Matched body half-points");
    using var matchedPackage = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(matchedResult.Content), false);
    var matchedErrors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(matchedPackage).ToList();
    Require(matchedErrors.Count == 0, "Matched DOCX schema validation: " + string.Join("; ", matchedErrors.Select(e => e.Description)));
}
foreach (var invalidStyle in new[] { new EditorExportDocumentStyle { FontFamily = "Arial;url(evil)" },
    new EditorExportDocumentStyle { BorderColor = "red; }" }, new EditorExportDocumentStyle { LineHeight = double.NaN } })
{
    foreach (var styleFormat in new[] { EditorExportFormat.Pdf, EditorExportFormat.Docx })
    {
        var styleRejected = false;
        try { await exporter.ExportAsync(matchedHtml, styleFormat, new EditorExportOptions { DocumentStyle = invalidStyle }); }
        catch (ArgumentException) { styleRejected = true; }
        Require(styleRejected, "Invalid matched document style rejected");
    }
    var isolatedExcel = await exporter.ExportAsync(matchedHtml, EditorExportFormat.Xlsx, new EditorExportOptions { DocumentStyle = invalidStyle });
    Require(isolatedExcel.Content.Length > 0, "Excel ignores matched document style");
}
var wordOverride = await exporter.ExportAsync("<table><tr><td style='font-size:14pt'>Explicit point size</td><td>Default</td></tr></table>",
    EditorExportFormat.Docx, new EditorExportOptions { WordFontSize = 11, WordTableFontSize = 8, WordTableCellPadding = 0 });
using (var archive = new ZipArchive(new MemoryStream(wordOverride.Content), ZipArchiveMode.Read))
{
    var xml = Read(archive, "word/document.xml");
    Require(xml.Contains("w:val=\"28\"") && xml.Contains("w:val=\"16\""), "Word respects point sizes and explicit override");
}
foreach (var invalid in new[] { new EditorExportOptions { WordFontSize = 7 }, new EditorExportOptions { WordTableFontSize = 37 },
    new EditorExportOptions { WordTableCellPadding = -1 }, new EditorExportOptions { WordTableCellPadding = 25 },
    new EditorExportOptions { WordLayout = (EditorWordLayout)99 } })
{
    var invalidRejected = false;
    try { await exporter.ExportAsync(html, EditorExportFormat.Docx, invalid); }
    catch (ArgumentOutOfRangeException) { invalidRejected = true; }
    Require(invalidRejected, "Invalid Word options rejected");
    var unaffected = await exporter.ExportAsync(html, EditorExportFormat.Xlsx, invalid);
    using var archive = new ZipArchive(new MemoryStream(unaffected.Content), ZipArchiveMode.Read);
    Require(Read(archive, "xl/worksheets/sheet1.xml").Contains("EGM-001"), "Excel ignores Word options");
}

// 0.8.6: opt-in document import; no host or network dependency.
var importedWord = EditorDocumentImporter.Import("sample.docx", word.Content);
Require(importedWord.Html.Contains("Export test") && importedWord.Html.Contains("EGM-001"), "DOCX import retains body and table text");
Require(importedWord.Html.Contains("<table>") && importedWord.Html.Contains("<strong>") && importedWord.Html.Contains("Bold"), "DOCX import retains editable table and bold text");
Require(importedWord.Html.Contains("data:image/png;base64,") && importedWord.Html.Contains("Document figure"), "DOCX import retains embedded raster image");
Require(importedWord.Html.Contains("data-azunt-page-break"), "DOCX import retains explicit page break");
var importedText = EditorDocumentImporter.Import("sample.txt", Encoding.UTF8.GetBytes("First <tag>\r\n\r\nSecond & last"));
Require(importedText.Html.Contains("&lt;tag&gt;") && importedText.Html.Contains("<p><br></p>") && importedText.Html.Contains("&amp; last"), "Text import escapes markup and preserves paragraphs");
var importedHtml = EditorDocumentImporter.Import("sample.html", Encoding.UTF8.GetBytes("<html><head><title>Hidden</title></head><body><p onclick='bad()'>Keep</p><img src='https://example.com/pixel'><script>bad()</script></body></html>"));
Require(importedHtml.Html.Contains("Keep") && !importedHtml.Html.Contains("Hidden") && !importedHtml.Html.Contains("bad()") && !importedHtml.Html.Contains("example.com"), "HTML import sanitizes and excludes external images");
foreach (var badImport in new[] { ("bad.docx", Encoding.UTF8.GetBytes("not a zip")), ("bad.exe", new byte[] { 1 }), ("empty.txt", Array.Empty<byte>()), ("large.txt", new byte[EditorDocumentImporter.MaxFileBytes + 1]) })
{
    var importRejected = false;
    try { EditorDocumentImporter.Import(badImport.Item1, badImport.Item2); }
    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException) { importRejected = true; }
    Require(importRejected, "Invalid import rejected: " + badImport.Item1);
}
using (var invalidZip = new MemoryStream())
{
    using (var archive = new ZipArchive(invalidZip, ZipArchiveMode.Create, leaveOpen: true))
    using (var writer = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open()))
        writer.Write("<!DOCTYPE Types [<!ENTITY x SYSTEM 'file:///secret'>]><Types>&x;</Types>");
    var xmlDtdRejected = false;
    try { EditorDocumentImporter.Import("xxe.docx", invalidZip.ToArray()); }
    catch (System.Xml.XmlException) { xmlDtdRejected = true; }
    Require(xmlDtdRejected, "DOCX XML DTD rejected");
}

if (Environment.GetEnvironmentVariable("AZUNT_TEST_PDF") == "1")
{
    var pdf = await exporter.ExportAsync(html, EditorExportFormat.Pdf,
        new EditorExportOptions { ShowPageNumbers = true, WordFontSize = 7 });
    Require(Encoding.ASCII.GetString(pdf.Content, 0, 5) == "%PDF-", "PDF header");
    var report = "<h2>PDF density test</h2><table><tbody><tr><th>Report number</th><th>Department</th></tr>" +
        string.Concat(Enumerable.Range(1, 80).Select(i => $"<tr><td>R-{i:D6}</td><td>Accounting</td></tr>")) +
        "</tbody></table><p>Document end</p>";
    var outputDirectory = Environment.GetEnvironmentVariable("AZUNT_TEST_PDF_OUTPUT");
    foreach (var matchedPdfId in new[] { "Standard", "BrowserDefault", "CompactReport", "BusinessDocument", "Minimal" })
    {
        var matchedPdf = await exporter.ExportAsync(report, EditorExportFormat.Pdf, new EditorExportOptions
        { DocumentStyle = EditorExportDocumentStyle.FromPreset(matchedPdfId), PromotePdfTableHeaders = true });
        Require(Encoding.ASCII.GetString(matchedPdf.Content, 0, 5) == "%PDF-", "Matched document PDF header");
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, $"Matched-{matchedPdfId}.pdf"), matchedPdf.Content);
        }
    }
    foreach (var layout in Enum.GetValues<EditorPdfLayout>())
    {
        var result = await exporter.ExportAsync(report, EditorExportFormat.Pdf, new EditorExportOptions
        {
            PdfLayout = layout, PromotePdfTableHeaders = true, ShowPageNumbers = true, PaperSize = "Letter"
        });
        Require(Encoding.ASCII.GetString(result.Content, 0, 5) == "%PDF-", $"{layout} PDF header");
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, $"{layout}.pdf"), result.Content);
        }
    }
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
