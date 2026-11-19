using WaNut.Core.Hosting;
using WaNut.Core.Hotplug.Preflight;
using WaNut.Core.Properties;
using WaNut.Core.Storage;

namespace WaNut.Core.Hotplug;

/// <summary>What is in the world's datapacks folder on the VPS right now, and what the running server has enabled.</summary>
public static class LiveDatapacks
{
    /// <summary>The level-name folder from server.properties ("world" when unset).</summary>
    public static async Task<string> WorldFolderAsync(IServerFileSystem files, CancellationToken cancellationToken = default)
    {
        string? level = null;
        try
        {
            level = (await PropertiesFile.LoadAsync(files, "server.properties", cancellationToken).ConfigureAwait(false)).Get("level-name");
        }
        catch (FileNotFoundException)
        {
        }

        return string.IsNullOrWhiteSpace(level) ? "world" : level.Trim();
    }

    /// <summary>The world's datapacks folder, relative to the server folder.</summary>
    public static async Task<string> FolderAsync(IServerFileSystem files, CancellationToken cancellationToken = default) =>
        ServerPath.Combine(ServerPath.Normalize(await WorldFolderAsync(files, cancellationToken).ConfigureAwait(false)), HotplugWorkspace.DatapacksName);

    /// <summary>Every zip and folder there, by name. <paramref name="console"/> (when the server runs) says which are enabled.</summary>
    public static async Task<IReadOnlyList<LiveDatapack>> ListAsync(IServerFileSystem files, IServerConsole? console, CancellationToken cancellationToken = default)
    {
        var folder = await FolderAsync(files, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ServerFileNode> listed;
        try
        {
            listed = await files.ListDirectoryAsync(folder, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        var enabled = console is null ? null : await ConsoleQuery.EnabledPacksAsync(console, DatapackHotplugOrchestrator.DefaultConsoleTimeout, cancellationToken).ConfigureAwait(false);
        return listed
            .Where(n => n.IsDirectory || n.Name.EndsWith(HotplugNames.Extension, StringComparison.OrdinalIgnoreCase))
            .Select(n => new LiveDatapack(n.Name, n.IsDirectory, n.SizeBytes, n.LastModifiedUtc, enabled?.Contains(HotplugNames.PackId(n.Name))))
            .OrderBy(p => p.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Downloads one zip to <paramref name="tempFolder"/> and reads its pack.mcmeta; null when that fails.</summary>
    public static async Task<PackMetadata?> ReadMetadataAsync(IServerFileSystem files, string datapacksFolder, LiveDatapack pack, string tempFolder, CancellationToken cancellationToken = default)
    {
        if (pack.IsFolder)
        {
            try
            {
                var text = await files.ReadAllTextAsync(ServerPath.Combine(datapacksFolder, pack.FileName + "/" + PackMetadata.FileName), cancellationToken).ConfigureAwait(false);
                return PackMetadata.TryParse(text, out _);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        Directory.CreateDirectory(tempFolder);
        var local = Path.Combine(tempFolder, Guid.NewGuid().ToString("N") + HotplugNames.Extension);
        try
        {
            await files.DownloadFileAsync(ServerPath.Combine(datapacksFolder, pack.FileName), local, cancellationToken: cancellationToken).ConfigureAwait(false);
            return (await DatapackInspector.InspectAsync(local, null, cancellationToken).ConfigureAwait(false)).Metadata;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        finally
        {
            File.Delete(local);
        }
    }

    /// <summary>The newest audit lines first; empty when there are none yet.</summary>
    public static async Task<IReadOnlyList<HotplugAuditEntry>> AuditAsync(IServerFileSystem files, CancellationToken cancellationToken = default)
    {
        var world = ServerPath.Normalize(await WorldFolderAsync(files, cancellationToken).ConfigureAwait(false));
        foreach (var sameMount in new[] { true, false })
        {
            var workspace = HotplugWorkspaceInitializer.Plan(world, sameMount);
            try
            {
                return HotplugAuditEntry.ParseAll(await files.ReadAllTextAsync(workspace.AuditFile, cancellationToken).ConfigureAwait(false));
            }
            catch (FileNotFoundException)
            {
            }
        }

        return [];
    }
}
