namespace Azunt.Editor.Models;

/// <summary>Host-defined command shown in the editor menu or quick toolbar.</summary>
public sealed class EditorCustomCommand
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>File, Edit, Insert, Format, Table, Tools or Export.</summary>
    public string Group { get; set; } = "Tools";
    /// <summary>Menu or Toolbar.</summary>
    public string Placement { get; set; } = "Menu";
    /// <summary>Optional short text/symbol for commands placed on the quick toolbar.</summary>
    public string? Icon { get; set; }
    /// <summary>Disable this command when the editor is read-only.</summary>
    public bool RequiresEdit { get; set; }
}
