namespace Azunt.Editor.Models;

/// <summary>Determines whether Azunt.Editor is editable or displays read-only document content.</summary>
public enum EditorMode
{
    /// <summary>Full editor with formatting tools and document editing (the default).</summary>
    Edit = 0,

    /// <summary>Read-only document viewer without editor controls.</summary>
    View = 1
}
