using System.Text;

namespace WaNut.Core.Hotplug.Readiness;

/// <summary>An inbox folder on this PC.</summary>
public sealed class LocalReadinessSource(string folder) : IReadinessSource
{
    public Task<IReadOnlyList<ReadinessEntry>> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReadinessEntry> entries = [];
        if (Directory.Exists(folder))
        {
            var found = new List<ReadinessEntry>();
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
            {
                // A file deleted between listing and reading its length throws; it is simply gone.
                try
                {
                    found.Add(new ReadinessEntry(file.Name, file.Length, file.LastWriteTimeUtc));
                }
                catch (FileNotFoundException)
                {
                }
            }

            entries = found;
        }

        return Task.FromResult(entries);
    }

    public Task<bool> CanReadExclusivelyAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(folder, name), FileMode.Open, FileAccess.Read, FileShare.None);
            return Task.FromResult(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    public Task<string> ReadTextAsync(string name, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(Path.Combine(folder, name), Encoding.UTF8, cancellationToken);
}
