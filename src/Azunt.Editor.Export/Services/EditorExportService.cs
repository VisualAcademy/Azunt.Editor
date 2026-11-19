using Azunt.Editor.Export.Excel;
using Azunt.Editor.Export.Html;
using Azunt.Editor.Export.Models;
using Azunt.Editor.Export.Pdf;
using Azunt.Editor.Export.Word;
using Microsoft.Extensions.DependencyInjection;

namespace Azunt.Editor.Export.Services;

internal sealed class EditorExportService(PdfDocumentExporter pdf, IEnumerable<IEditorImageResolver> resolvers) : IEditorExportService
{
    public async Task<EditorExportResult> ExportAsync(string html, EditorExportFormat format,
        EditorExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new EditorExportOptions();
        var document = SafeEditorDocument.Parse(html);
        // Local, host-owned images are resolved in memory. Playwright is still
        // offline, and no URL is ever fetched by the export engine.
        var resolver = resolvers.FirstOrDefault();
        if (resolver is not null)
        {
            var count = 0;
            var total = 0;
            foreach (var image in document.Root.DescendantsAndSelf().Where(n => n.Name == "img"))
            {
                var src = image.Attribute("src");
                if (!SafeEditorDocument.IsSafeLocalImageReference(src)) continue;
                if (++count > 32) break;
                var resolved = await resolver.ResolveAsync(src, cancellationToken);
                if (resolved is null || resolved.Content.Length is <= 0 or > 2 * 1024 * 1024) continue;
                total += resolved.Content.Length;
                if (total > 12 * 1024 * 1024) break;
                var imageMime = resolved.ContentType.ToLowerInvariant();
                if (imageMime is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp")) continue;
                var inline = $"data:{imageMime};base64,{Convert.ToBase64String(resolved.Content)}";
                image.Attributes["src"] = inline;
                if (!SafeEditorDocument.TryInlineImage(image, out _, out _))
                    image.Attributes["src"] = string.Empty;
            }
        }
        var (content, mime, extension) = format switch
        {
            EditorExportFormat.Pdf => (await pdf.ExportAsync(document, options, cancellationToken),
                "application/pdf", ".pdf"),
            EditorExportFormat.Docx => (DocxDocumentExporter.Export(document, options),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx"),
            EditorExportFormat.Xlsx => (XlsxDocumentExporter.Export(document),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx"),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new EditorExportResult(CleanName(options.FileName) + extension, mime, content);
    }

    private static string CleanName(string raw)
    {
        var baseName = Path.GetFileNameWithoutExtension(raw ?? "Document");
        var safe = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').Take(80).ToArray()).Trim();
        return safe.Length == 0 ? "Document" : safe;
    }
}

public static class EditorExportServiceCollectionExtensions
{
    public static IServiceCollection AddAzuntEditorExport(this IServiceCollection services,
        Action<EditorPdfOptions>? configurePdf = null)
    {
        services.AddOptions<EditorPdfOptions>();
        if (configurePdf is not null) services.Configure(configurePdf);
        services.AddSingleton<PdfDocumentExporter>();
        services.AddSingleton<IEditorExportService, EditorExportService>();
        return services;
    }
}
