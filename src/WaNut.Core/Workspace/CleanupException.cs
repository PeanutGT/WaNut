namespace WaNut.Core.Workspace;

/// <summary>A clean-up item would touch the server's world; nothing was changed.</summary>
public sealed class CleanupException(string path)
    : InvalidOperationException($"'{path}' is part of the server's world and is never deleted by the clean-up.")
{
    public string Path { get; } = path;
}
