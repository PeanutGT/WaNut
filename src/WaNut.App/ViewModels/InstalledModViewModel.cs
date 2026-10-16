using WaNut.App.Resources;
using WaNut.Core.Mods;

namespace WaNut.App.ViewModels;

/// <summary>One jar in the server's mods or plugins folder.</summary>
public sealed class InstalledModViewModel(InstalledMod mod)
{
    public InstalledMod Mod { get; } = mod;

    public string Name => Mod.DisplayName;

    /// <summary>The file name under a Modrinth title; "Added by hand" for jars WaNut did not install.</summary>
    public string DetailText => Mod.Title is null ? Text.Get("Mods_ByHand") : Mod.FileName;

    public string AccessibleName => $"{Name}, {DetailText}";

    public string RemoveLabel => string.Format(Text.Get("Mods_RemoveLabel"), Name);

    public override string ToString() => AccessibleName;
}
