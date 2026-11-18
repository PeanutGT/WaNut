using WaNut.Core.Storage;

namespace WaNut.Core.Workspace;

/// <summary>One line of the folder tree: an entry, how deep it is and, for a folder, whether it is open.</summary>
public sealed record FolderRow(ServerFileNode Node, int Depth, bool IsExpanded);
