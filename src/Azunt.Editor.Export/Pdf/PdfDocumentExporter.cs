using Azunt.Editor.Export.Html;
using Azunt.Editor.Export.Models;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Text.Encodings.Web;

namespace Azunt.Editor.Export.Pdf;

internal sealed class PdfDocumentExporter : IDisposable
{
    private readonly EditorPdfOptions _settings;
    private readonly SemaphoreSlim _slots;

    public PdfDocumentExporter(IOptions<EditorPdfOptions> options)
    {
        _settings = options.Value;
        _slots = new SemaphoreSlim(Math.Clamp(_settings.MaxConcurrentExports, 1, 16));
    }

    public async Task<byte[]> ExportAsync(SafeEditorDocument document, EditorExportOptions options, CancellationToken token)
    {
        await _slots.WaitAsync(token);
        try
        {
            using var playwright = await Playwright.CreateAsync();
            var launch = new BrowserTypeLaunchOptions { Headless = true };
            if (!string.IsNullOrWhiteSpace(_settings.BrowserChannel))
                launch.Channel = _settings.BrowserChannel;
            if (!string.IsNullOrWhiteSpace(_settings.ExecutablePath))
                launch.ExecutablePath = _settings.ExecutablePath;
            IBrowser browser;
            try
            {
                browser = await playwright.Chromium.LaunchAsync(launch);
            }
            catch (PlaywrightException ex) when (
                ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Executable not found", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The configured Playwright browser is missing. Install Chromium for this package version " +
                    "(run the Azunt.Web build output playwright.ps1 install chromium), or configure " +
                    "EditorPdfOptions.BrowserChannel = \"chrome\" / \"msedge\" to use an installed browser.", ex);
            }
            await using var browserLifetime = browser;
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                JavaScriptEnabled = false,
                Offline = true,
                ServiceWorkers = ServiceWorkerPolicy.Block
            });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(_settings.RenderTimeoutMilliseconds);
            var title = HtmlEncoder.Default.Encode(options.Title);
            var safeHtml = document.ToSafeHtml();
            var html = $$"""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><title>{{title}}</title>
                <style>
                  *{box-sizing:border-box}body{font-family:Arial,'Malgun Gothic',sans-serif;color:#1e293b;font-size:12pt;line-height:1.45}
                  p,li{overflow-wrap:anywhere}h1,h2,h3,h4{break-after:avoid}
                  table{border-collapse:collapse;width:100%;margin:10px 0;break-inside:auto}
                  thead{display:table-header-group}tfoot{display:table-footer-group}
                  td,th{border:1px solid #94a3b8;padding:6px;vertical-align:top;overflow-wrap:anywhere;break-inside:avoid}
                  tr{break-inside:avoid;page-break-inside:avoid}
                  img{max-width:100%;height:auto;break-inside:avoid;page-break-inside:avoid}
                  p{orphans:2;widows:2}h1,h2,h3,h4{page-break-after:avoid}
                  pre{white-space:pre-wrap;overflow-wrap:anywhere}blockquote{border-left:3px solid #cbd5e1;padding-left:15px;margin-left:0}
                  a{color:#1d4ed8}hr{border:0;border-top:1px solid #cbd5e1}
                  hr[data-azunt-page-break="true"]{break-before:page;page-break-before:always;border:0;margin:0;height:0}
                </style></head><body>{{safeHtml}}</body></html>
                """;
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });
            token.ThrowIfCancellationRequested();
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Print });
            var paper = options.PaperSize is "Letter" or "Legal" ? options.PaperSize : "A4";
            var margin = options.Margin switch { "narrow" => "8mm", "wide" => "25mm", _ => "16mm" };
            var pdf = await page.PdfAsync(new PagePdfOptions
            {
                Format = paper,
                Landscape = string.Equals(options.Orientation, "landscape", StringComparison.OrdinalIgnoreCase),
                PrintBackground = true,
                DisplayHeaderFooter = options.ShowPageNumbers,
                FooterTemplate = options.ShowPageNumbers
                    ? "<div style='width:100%;font:9px Arial,sans-serif;color:#64748b;text-align:center'>" +
                      "<span class='pageNumber'></span> / <span class='totalPages'></span></div>"
                    : null,
                Margin = new Margin
                {
                    Top = margin,
                    Bottom = options.ShowPageNumbers && margin == "8mm" ? "12mm" : margin,
                    Left = margin,
                    Right = margin
                }
            });
            token.ThrowIfCancellationRequested();
            return pdf;
        }
        finally { _slots.Release(); }
    }

    public void Dispose() => _slots.Dispose();
}
