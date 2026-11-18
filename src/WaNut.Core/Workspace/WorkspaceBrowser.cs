using WaNut.Core.Storage;

namespace WaNut.Core.Workspace;

/// <summary>
/// What the Files tab does with a server folder, on this PC or a VPS alike: the folder tree, how much space each kind
/// of file takes, and reading and saving a text file without overwriting a change someone else made meanwhile.
/// </summary>
public sealed class WorkspaceBrowser(IServerFileSystem fileSystem)
{
    public IServerFileSystem FileSystem => fileSystem;

    public FolderTree Tree { get; } = new(fileSystem);

    /// <summary>Lists every file once (a single command on a VPS) and adds up the sizes.</summary>
    public async Task<WorkspaceUsage> MeasureAsync(CancellationToken cancellationToken = default) =>
        WorkspaceUsage.From(await fileSystem.ListDirectoryAsync(string.Empty, true, cancellationToken).ConfigureAwait(false));

    /// <exception cref="TextEditException">The entry is a folder, too big, or not a text file.</exception>
    public async Task<TextDocument> OpenTextAsync(ServerFileNode node, CancellationToken cancellationToken = default)
    {
        if (TextFilePolicy.Check(node) is { } problem)
        {
            throw new TextEditException(problem);
        }

        var text = await fileSystem.ReadAllTextAsync(node.Path, cancellationToken).ConfigureAwait(false);
        if (text.Contains('\0', StringComparison.Ordinal))
        {
            throw new TextEditException(TextEditProblem.NotText);
        }

        // The listing's size and time are what a later save compares against, so they must describe the text just read.
        var current = await FindAsync(node.Path, cancellationToken).ConfigureAwait(false) ?? node;
        return new TextDocument(node.Path, text, current.SizeBytes, current.LastModifiedUtc, LineEndings.UsesCrLf(text));
    }

    /// <summary>
    /// Writes <paramref name="text"/> over the file. Unless <paramref name="overwriteChanges"/>, a file that changed
    /// since <paramref name="opened"/> is left alone and <see cref="TextSaveOutcome.ChangedMeanwhile"/> comes back.
    /// </summary>
    public async Task<TextSaveResult> SaveTextAsync(TextDocument opened, string text, bool overwriteChanges = false, CancellationToken cancellationToken = default)
    {
        if (!overwriteChanges)
        {
            var now = await FindAsync(opened.Path, cancellationToken).ConfigureAwait(false);
            if (now is null || now.SizeBytes != opened.SizeBytes || now.LastModifiedUtc != opened.ModifiedUtc)
            {
                return new TextSaveResult(TextSaveOutcome.ChangedMeanwhile, null);
            }
        }

        var content = LineEndings.Apply(text, opened.CrLf);
        await fileSystem.WriteAllTextAsync(opened.Path, content, cancellationToken).ConfigureAwait(false);
        var written = await FindAsync(opened.Path, cancellationToken).ConfigureAwait(false);
        return new TextSaveResult(
            TextSaveOutcome.Saved,
            new TextDocument(opened.Path, content, written?.SizeBytes ?? content.Length, written?.LastModifiedUtc ?? DateTime.UtcNow, opened.CrLf));
    }

    private async Task<ServerFileNode?> FindAsync(string path, CancellationToken cancellationToken)
    {
        var slash = path.LastIndexOf('/');
        var folder = slash < 0 ? string.Empty : path[..slash];
        try
        {
            var siblings = await fileSystem.ListDirectoryAsync(folder, false, cancellationToken).ConfigureAwait(false);
            return siblings.FirstOrDefault(n => n.Path == path);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
