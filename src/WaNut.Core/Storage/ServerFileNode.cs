namespace WaNut.Core.Storage;

/// <summary>
/// One file or folder in a server folder. <see cref="Path"/> is relative to the file system's workspace root and uses '/'.
/// </summary>
public sealed record ServerFileNode(
    string Name,
    string Path,
    bool IsDirectory,
    long SizeBytes,
    DateTime LastModifiedUtc,
    McFileCategory Category);
