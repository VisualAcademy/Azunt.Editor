using Azunt.Editor.Export.Excel;
using Azunt.Editor.Export.Html;
using Azunt.Editor.Export.Models;
using Azunt.Editor.Export.Pdf;
using Azunt.Editor.Export.Word;
using Microsoft.Extensions.DependencyInjection;

namespace Azunt.Editor.Export.Services;

internal sealed class EditorExportService(PdfDocumentExporter pdf) : IEditorExportService
{
    public async Task<EditorExportResult> ExportAsync(string html, EditorExportFormat format,
        EditorExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new EditorExportOptions();
        var document = SafeEditorDocument.Parse(html);
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
