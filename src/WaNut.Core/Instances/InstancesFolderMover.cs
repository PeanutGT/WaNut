namespace WaNut.Core.Instances;

/// <summary>
/// Moves every server from one servers folder to another, e.g. from %APPDATA% to drive D:.
/// Each server is fully in place at the new folder before its old copy is deleted, so a failure
/// (full disk, file in use) never leaves a server half-moved.
/// </summary>
public static class InstancesFolderMover
{
    /// <summary>Why a move cannot start, or null when it can.</summary>
    public static MoveProblem? Check(string fromDir, string toDir)
    {
        var from = Normalize(fromDir);
        var to = Normalize(toDir);

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return MoveProblem.SameFolder;
        }

        if (IsInside(to, from) || IsInside(from, to))
        {
            return MoveProblem.NestedFolder;
        }

        if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any())
        {
            return MoveProblem.TargetNotEmpty;
        }

        return null;
    }

    /// <summary>Moves each server folder in <paramref name="fromDir"/> into <paramref name="toDir"/>, reporting each server's folder name first.</summary>
    public static void Move(string fromDir, string toDir, IProgress<string>? progress = null)
    {
        if (Check(fromDir, toDir) is { } problem)
        {
            throw new InvalidOperationException($"The servers folder cannot be moved: {problem}.");
        }

        Directory.CreateDirectory(toDir);
        if (!Directory.Exists(fromDir))
        {
            return;
        }

        var sameDrive = string.Equals(Path.GetPathRoot(Normalize(fromDir)), Path.GetPathRoot(Normalize(toDir)), StringComparison.OrdinalIgnoreCase);
        var moved = new List<string>();
        try
        {
            foreach (var source in Directory.EnumerateDirectories(fromDir).ToList())
            {
                var name = Path.GetFileName(source);
                progress?.Report(name);
                MoveOne(source, Path.Combine(toDir, name), sameDrive);
                moved.Add(name);
            }
        }
        catch
        {
            // Put the servers already moved back, so every server stays in the folder WaNut still uses.
            foreach (var name in moved)
            {
                try
                {
                    MoveOne(Path.Combine(toDir, name), Path.Combine(fromDir, name), sameDrive);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Best effort: the original error is the one worth showing.
                }
            }

            throw;
        }

        if (!Directory.EnumerateFileSystemEntries(fromDir).Any())
        {
            Directory.Delete(fromDir);
        }
    }

    private static void MoveOne(string source, string target, bool sameDrive)
    {
        if (sameDrive)
        {
            Directory.Move(source, target);
            return;
        }

        // Copy under a temporary name first, so a half-copied folder never looks like a server.
        var partial = target + ".moving";
        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }

        try
        {
            CopyDirectory(source, partial);
        }
        catch
        {
            if (Directory.Exists(partial))
            {
                Directory.Delete(partial, recursive: true);
            }

            throw;
        }

        Directory.Move(partial, target);
        Directory.Delete(source, recursive: true);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    private static string Normalize(string dir) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));

    private static bool IsInside(string dir, string parent) =>
        dir.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

public enum MoveProblem
{
    SameFolder,
    NestedFolder,
    TargetNotEmpty,
}
