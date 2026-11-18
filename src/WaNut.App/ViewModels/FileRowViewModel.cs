using System.Windows;
using WaNut.App.Resources;
using WaNut.Core.Storage;
using WaNut.Core.Workspace;

namespace WaNut.App.ViewModels;

/// <summary>One line of the Files tab's tree.</summary>
public sealed class FileRowViewModel(FolderRow row)
{
    private const double IndentPerLevel = 18;

    public FolderRow Row => row;

    public ServerFileNode Node => row.Node;

    public string Path => row.Node.Path;

    public string Name => row.Node.Name;

    public bool IsDirectory => row.Node.IsDirectory;

    public Thickness Indent => new(row.Depth * IndentPerLevel, 0, 0, 0);

    public string Glyph => row.IsExpanded ? "▾" : "▸";

    public string CategoryText => row.Node.Category == McFileCategory.Other ? string.Empty : Text.Get($"Files_Category_{row.Node.Category}");

    public string SizeText => row.Node.IsDirectory ? string.Empty : ByteSize.Format(row.Node.SizeBytes);

    public string ToggleName => string.Format(Text.Get(row.IsExpanded ? "Files_CloseFolder" : "Files_OpenFolder"), Name);

    /// <summary>What screen readers read for the row.</summary>
    public string AccessibleName => IsDirectory ? $"{Name}, {Text.Get("Files_FolderKind")}" : $"{Name}, {SizeText}";

    public override string ToString() => AccessibleName;
}
