namespace WaNut.Core.Storage;

/// <summary>
/// Reads and changes the files of one server folder, wherever it is: on this PC (<see cref="LocalFileSystemAdapter"/>)
/// or on a VPS. Every path is relative to <see cref="WorkspaceRoot"/> and uses '/' (see <see cref="ServerPath"/>);
/// a path that leaves the folder is refused with <see cref="ArgumentException"/>.
/// </summary>
public interface IServerFileSystem : IAsyncDisposable
{
    bool IsRemote { get; }

    /// <summary>Where the server folder is, for display and logs: a Windows path or a path on the VPS.</summary>
    string WorkspaceRoot { get; }

    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>The entries of a folder, folders first and then by name. <paramref name="recursive"/> includes everything below it. Symbolic links are left out.</summary>
    /// <exception cref="DirectoryNotFoundException">The folder doesn't exist.</exception>
    Task<IReadOnlyList<ServerFileNode>> ListDirectoryAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default);

    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    Task<string> ReadAllTextAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Writes UTF-8 text without a byte order mark under a temporary name and then renames it, so an interrupted write never leaves a half-written file. Creates missing folders.</summary>
    Task WriteAllTextAsync(string relativePath, string content, CancellationToken cancellationToken = default);

    /// <summary>Copies a file from this PC into the server folder (same atomic write as <see cref="WriteAllTextAsync"/>).</summary>
    Task UploadFileAsync(string localPath, string relativePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Copies a file from the server folder to this PC. <paramref name="progress"/> reports 0 to 1.</summary>
    Task DownloadFileAsync(string relativePath, string localPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes files and folders (with everything in them). A path that is already gone is skipped; the server folder itself is refused.</summary>
    Task DeleteBatchAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);

    /// <summary>Packs files and folders into one .tar.gz inside the server folder and returns its normalized relative path.</summary>
    Task<string> CreateArchiveAsync(IEnumerable<string> targetRelativePaths, string outputArchiveRelativePath, CancellationToken cancellationToken = default);

    /// <summary>Moves or renames a file inside the server folder, creating the target's folder. Never replaces a file.</summary>
    /// <exception cref="FileNotFoundException"><paramref name="fromRelativePath"/> doesn't exist.</exception>
    /// <exception cref="IOException"><paramref name="toRelativePath"/> already exists.</exception>
    Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken cancellationToken = default);

    /// <summary>The SHA-512 of each file, lower-case hex, in the order given. A VPS works them out itself, so nothing is downloaded.</summary>
    /// <exception cref="FileNotFoundException">One of the files doesn't exist.</exception>
    Task<IReadOnlyList<string>> HashSha512Async(IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default);
}
