namespace Azunt.Editor.Models;

/// <summary>Context for an opt-in selection-aware host command. Tokens are single-use and editor-local.</summary>
public sealed record EditorCommandContext(string CommandId, string? SelectionToken);
