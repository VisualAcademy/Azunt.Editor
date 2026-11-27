namespace Azunt.Editor.Export.Models;

[Flags]
public enum EditorExportFormats
{
    None = 0, Pdf = 1, Word = 2, Excel = 4, All = Pdf | Word | Excel
}

/// <summary>Common export command identifiers and labels; independent of the editor UI package.</summary>
public static class EditorExportMenu
{
    public const string PdfCommand = "azuntExportPdf";
    public const string WordCommand = "azuntExportWord";
    public const string ExcelCommand = "azuntExportExcel";

    public static bool TryGetFormat(string? command, out EditorExportFormat format)
    {
        switch (command)
        {
            case PdfCommand: format = EditorExportFormat.Pdf; return true;
            case WordCommand: format = EditorExportFormat.Docx; return true;
            case ExcelCommand: format = EditorExportFormat.Xlsx; return true;
            default: format = default; return false;
        }
    }

    public static bool IsEnabled(EditorExportFormats formats, EditorExportFormat format) => format switch
    {
        EditorExportFormat.Pdf => (formats & EditorExportFormats.Pdf) != 0,
        EditorExportFormat.Docx => (formats & EditorExportFormats.Word) != 0,
        EditorExportFormat.Xlsx => (formats & EditorExportFormats.Excel) != 0,
        _ => false
    };

    public static IReadOnlyList<(string Id, string Label, EditorExportFormat Format)> GetCommands(
        EditorExportFormats formats = EditorExportFormats.All)
    {
        var commands = new (string Id, string Label, EditorExportFormat Format)[]
        {
            (PdfCommand, "Download PDF", EditorExportFormat.Pdf),
            (WordCommand, "Download Word", EditorExportFormat.Docx),
            (ExcelCommand, "Download Excel", EditorExportFormat.Xlsx)
        };
        return commands.Where(command => IsEnabled(formats, command.Format)).ToArray();
    }

    /// <summary>Adapt the neutral commands to a host command type without a package dependency.</summary>
    public static IReadOnlyList<T> CreateCommands<T>(Func<string, string, T> factory,
        EditorExportFormats formats = EditorExportFormats.All)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return GetCommands(formats).Select(command => factory(command.Id, command.Label)).ToArray();
    }

    /// <summary>Accept only the named formats used by the form buttons (not numeric enum values).</summary>
    public static bool TryParseFormFormat(string? value, out EditorExportFormat format)
    {
        if (string.Equals(value, "Pdf", StringComparison.OrdinalIgnoreCase)) { format = EditorExportFormat.Pdf; return true; }
        if (string.Equals(value, "Docx", StringComparison.OrdinalIgnoreCase)) { format = EditorExportFormat.Docx; return true; }
        if (string.Equals(value, "Xlsx", StringComparison.OrdinalIgnoreCase)) { format = EditorExportFormat.Xlsx; return true; }
        format = default;
        return false;
    }
}
