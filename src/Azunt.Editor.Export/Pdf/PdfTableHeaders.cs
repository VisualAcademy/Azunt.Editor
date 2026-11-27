using Azunt.Editor.Export.Html;

namespace Azunt.Editor.Export.Pdf;

internal static class PdfTableHeaders
{
    public static void Promote(SafeEditorDocument document)
    {
        // Snapshot the table list before reparenting any rows (including nested tables).
        foreach (var table in document.Root.DescendantsAndSelf().Where(n => n.Name == "table").ToArray())
        {
            // Never infer headers for tables with an existing header or unusual structure.
            if (table.Children.Any(n => n.Name == "thead")) continue;
            var groups = Elements(table);
            if (groups.Length == 0) continue;
            var first = groups[0];
            SafeEditorDocument.Node parent;
            SafeEditorDocument.Node row;
            if (first.Name == "tr" && groups.All(n => n.Name == "tr" || n.Name == "tfoot"))
            {
                parent = table;
                row = first;
                // Do not promote a table consisting only of a header row.
                if (groups.Count(n => n.Name == "tr") < 2) continue;
            }
            else if (first.Name == "tbody" && groups.All(n => n.Name == "tbody" || n.Name == "tfoot"))
            {
                parent = first;
                var rows = Elements(parent);
                if (rows.Length < 2 || rows.Any(n => n.Name != "tr")) continue;
                row = rows[0];
            }
            else continue;
            var cells = Elements(row);
            if (cells.Length == 0 || cells.Any(n => n.Name != "th")) continue;
            var header = new SafeEditorDocument.Node("thead");
            var index = table.Children.IndexOf(first);
            parent.Children.Remove(row);
            header.Children.Add(row);
            table.Children.Insert(index, header);
        }
    }

    private static SafeEditorDocument.Node[] Elements(SafeEditorDocument.Node parent)
        => parent.Children.Where(n => n.Name != "#text" || !string.IsNullOrWhiteSpace(n.Text)).ToArray();
}
