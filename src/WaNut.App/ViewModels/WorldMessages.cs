using System.IO;
using System.Net.Http;
using System.Text.Json;
using WaNut.App.Resources;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Mods;
using WaNut.Core.Worlds;

namespace WaNut.App.ViewModels;

/// <summary>The plain-language notes shown after an import or export.</summary>
internal static class WorldMessages
{
    public static string ImportNotes(WorldImportResult result)
    {
        var notes = new List<string>();
        if (result.InventoryProblem is { } problem)
        {
            notes.Add(Text.Get($"World_Inventory_{problem}"));
        }

        if (result.HasResourcePack)
        {
            notes.Add(Text.Get("World_ResourcePackHint"));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, notes);
    }

    /// <summary>What "Install the mods too" did, mod by mod; empty when the instance had nothing to install.</summary>
    public static string ModNotes(ModCopyResult result)
    {
        var notes = new List<string>();
        if (result.Installed.Count > 0)
        {
            notes.Add(string.Format(Text.Get("World_ModsInstalled"), result.Installed.Count, string.Join(", ", result.Installed)));
        }

        if (result.NoServerVersion.Count > 0)
        {
            notes.Add(string.Format(Text.Get("World_ModsNoVersion"), string.Join(", ", result.NoServerVersion)));
        }

        if (result.NotOnModrinth.Count > 0)
        {
            notes.Add(string.Format(Text.Get("World_ModsUnknown"), string.Join(", ", result.NotOnModrinth)));
        }

        if (result.ClientOnly.Count > 0)
        {
            notes.Add(string.Format(Text.Get("World_ModsClientOnly"), string.Join(", ", result.ClientOnly)));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, notes);
    }

    /// <summary>
    /// "Install the mods too" after the world is in place. A failure here never undoes the import: the world is
    /// fine, and the mods can still be added on the Mods tab.
    /// </summary>
    public static async Task<string> InstallModsAsync(
        ModLibrary library, string instanceDir, string gameVersion, WorldSource source, IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            var result = await library.InstallCopiesAsync(instanceDir, ModPlatform.For(source.ModServerType!.Value)!, gameVersion, source.ModPaths, progress, cancellationToken);
            return ModNotes(result);
        }
        catch (OperationCanceledException)
        {
            return Text.Get("World_ModsCancelled");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or DownloadVerificationException)
        {
            return string.Format(Text.Get("World_ModsFailed"), e.Message);
        }
    }

    /// <summary>Joins the import's notes and the mods' notes into one message.</summary>
    public static string Join(params string[] notes) =>
        string.Join(Environment.NewLine + Environment.NewLine, notes.Where(n => n.Length > 0));

    public static string Problem(WorldException e) => Text.Get($"World_Problem_{e.Problem}");
}
