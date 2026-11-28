using WaNut.Core.Instances;
using WaNut.Core.Storage;

namespace WaNut.Core.Mods;

/// <summary>
/// The file work <see cref="ModLibrary"/> does in a server folder, on this PC or straight on the VPS. Paths are relative
/// to the server folder and use '/'.
/// </summary>
internal interface IModFiles
{
    /// <summary>The names of the jars directly in <paramref name="folder"/>; none when it doesn't exist.</summary>
    Task<IReadOnlyList<string>> ListJarsAsync(string folder, CancellationToken cancellationToken);

    /// <summary>The SHA-512 of each file, lower-case hex, in order.</summary>
    Task<IReadOnlyList<string>> HashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>The file's text, or null when it doesn't exist.</summary>
    Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken);

    Task WriteTextAsync(string path, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a file made on this PC at <paramref name="path"/>: <paramref name="write"/> fills the local file it is given
    /// (a download, checked against its hash). A file already there is replaced only once that succeeded.
    /// </summary>
    Task ReceiveAsync(string path, Func<string, Task> write, CancellationToken cancellationToken);

    /// <summary>Copies a file from this PC to <paramref name="path"/>, which must not exist yet.</summary>
    Task CopyInAsync(string localPath, string path, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);
}

/// <summary>A server folder on this PC.</summary>
internal sealed class LocalModFiles(string instanceDir) : IModFiles
{
    public Task<IReadOnlyList<string>> ListJarsAsync(string folder, CancellationToken cancellationToken)
    {
        var full = Full(folder);
        IReadOnlyList<string> names = Directory.Exists(full)
            ? Directory.EnumerateFiles(full, "*.jar").Select(p => Path.GetFileName(p)).ToList()
            : [];
        return Task.FromResult(names);
    }

    public async Task<IReadOnlyList<string>> HashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var hashes = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            hashes.Add(await Task.Run(() => ModLibrary.HashFile(Full(path)), cancellationToken).ConfigureAwait(false));
        }

        return hashes;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) => Task.FromResult(File.Exists(Full(path)));

    public Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        var full = Full(path);
        return Task.FromResult(File.Exists(full) ? File.ReadAllText(full) : null);
    }

    public Task WriteTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        AtomicFile.WriteAllText(Full(path), text);
        return Task.CompletedTask;
    }

    public async Task ReceiveAsync(string path, Func<string, Task> write, CancellationToken cancellationToken)
    {
        var target = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
        {
            await write(target).ConfigureAwait(false);
            return;
        }

        // Replacing a jar: the old one stays until the new one is complete and checked.
        var temp = target + ".update";
        await write(temp).ConfigureAwait(false);
        File.Move(temp, target, overwrite: true);
    }

    public Task CopyInAsync(string localPath, string path, CancellationToken cancellationToken)
    {
        var target = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(localPath, target, overwrite: false);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        File.Delete(Full(path));
        return Task.CompletedTask;
    }

    private string Full(string path) => Path.Combine(instanceDir, ServerPath.Normalize(path).Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>
/// A server folder reached through <see cref="IServerFileSystem"/>, in practice the VPS copy while it is the newest:
/// nothing comes back to this PC first. New jars are downloaded here, checked, then sent up in one atomic write.
/// </summary>
internal sealed class ServerModFiles(IServerFileSystem files) : IModFiles
{
    public async Task<IReadOnlyList<string>> ListJarsAsync(string folder, CancellationToken cancellationToken)
    {
        try
        {
            return (await files.ListDirectoryAsync(folder, cancellationToken: cancellationToken).ConfigureAwait(false))
                .Where(n => !n.IsDirectory && n.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Name)
                .ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    public Task<IReadOnlyList<string>> HashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        files.HashSha512Async(paths, cancellationToken);

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) => files.ExistsAsync(path, cancellationToken);

    public async Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await files.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public Task WriteTextAsync(string path, string text, CancellationToken cancellationToken) => files.WriteAllTextAsync(path, text, cancellationToken);

    public async Task ReceiveAsync(string path, Func<string, Task> write, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "WaNut", "mod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var local = Path.Combine(folder, Path.GetFileName(path));
            await write(local).ConfigureAwait(false);

            // Written under a temporary name and renamed over any old jar, so a cut connection leaves the old one.
            await files.UploadFileAsync(local, path, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Only a temporary copy is left behind.
            }
        }
    }

    public async Task CopyInAsync(string localPath, string path, CancellationToken cancellationToken)
    {
        if (await files.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            throw new IOException($"'{path}' already exists in the server folder.");
        }

        await files.UploadFileAsync(localPath, path, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken) => files.DeleteBatchAsync([path], cancellationToken);
}
