using WaNut.Core.Storage;

namespace WaNut.Core.Workspace;

/// <summary>
/// The server folder as a list of lines to show, opened one folder at a time: a folder is listed only when it is
/// first expanded, so a world with thousands of region files costs nothing until someone opens it.
/// </summary>
public sealed class FolderTree(IServerFileSystem fileSystem)
{
    private readonly Dictionary<string, IReadOnlyList<ServerFileNode>> _loaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    /// <summary>What is on screen now, top to bottom.</summary>
    public IReadOnlyList<FolderRow> Rows { get; private set; } = [];

    public async Task LoadRootAsync(CancellationToken cancellationToken = default)
    {
        _loaded[string.Empty] = await fileSystem.ListDirectoryAsync(string.Empty, false, cancellationToken).ConfigureAwait(false);
        Rebuild();
    }

    /// <summary>Opens a closed folder (listing it the first time) or closes an open one.</summary>
    public async Task ToggleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!_expanded.Remove(path))
        {
            if (!_loaded.ContainsKey(path))
            {
                _loaded[path] = await fileSystem.ListDirectoryAsync(path, false, cancellationToken).ConfigureAwait(false);
            }

            _expanded.Add(path);
        }

        Rebuild();
    }

    /// <summary>Lists the root and every open folder again; folders that are gone close.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var fresh = new Dictionary<string, IReadOnlyList<ServerFileNode>>(StringComparer.Ordinal);
        fresh[string.Empty] = await fileSystem.ListDirectoryAsync(string.Empty, false, cancellationToken).ConfigureAwait(false);

        // Parents first, so a folder that disappeared takes its open children with it.
        foreach (var path in _expanded.OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p, StringComparer.Ordinal).ToList())
        {
            var parent = path.Contains('/', StringComparison.Ordinal) ? path[..path.LastIndexOf('/')] : string.Empty;
            if (!fresh.TryGetValue(parent, out var siblings) || !siblings.Any(n => n.IsDirectory && n.Path == path))
            {
                _expanded.Remove(path);
                continue;
            }

            try
            {
                fresh[path] = await fileSystem.ListDirectoryAsync(path, false, cancellationToken).ConfigureAwait(false);
            }
            catch (DirectoryNotFoundException)
            {
                _expanded.Remove(path);
            }
        }

        _loaded.Clear();
        foreach (var pair in fresh)
        {
            _loaded[pair.Key] = pair.Value;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        var rows = new List<FolderRow>();
        Add(string.Empty, 0, rows);
        Rows = rows;
    }

    private void Add(string folder, int depth, List<FolderRow> rows)
    {
        if (!_loaded.TryGetValue(folder, out var children))
        {
            return;
        }

        foreach (var node in children)
        {
            var open = node.IsDirectory && _expanded.Contains(node.Path);
            rows.Add(new FolderRow(node, depth, open));
            if (open)
            {
                Add(node.Path, depth + 1, rows);
            }
        }
    }
}
