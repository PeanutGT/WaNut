using System.IO;
using WaNut.App.Resources;
using WaNut.Core.Worlds;

namespace WaNut.App.ViewModels;

/// <summary>One world in the import list: where it came from and which version last saved it.</summary>
public sealed class WorldOptionViewModel(WorldSource source)
{
    public WorldSource Source { get; } = source;

    public string Title => Source.Details.LevelName;

    /// <summary>"1.21.1 · Modrinth App · Fabulously Optimized", or the file name for a world the user picked.</summary>
    public string Subtitle => string.Join(" · ", new[] { Source.Details.VersionName ?? Text.Get("World_UnknownVersion"), Where }.Where(s => s.Length > 0));

    public override string ToString() => $"{Title}, {Subtitle}";

    private string Where => Source.Location switch
    {
        null => Path.GetFileName(Source.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
        { InstanceName: null } location => LauncherName(location.Launcher),
        { } location => $"{LauncherName(location.Launcher)} · {location.InstanceName}",
    };

    internal static string LauncherName(LauncherKind launcher) => Text.Get($"World_Launcher_{launcher}");
}
