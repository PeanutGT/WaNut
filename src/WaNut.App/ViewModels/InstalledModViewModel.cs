using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Mods;

namespace WaNut.App.ViewModels;

/// <summary>One jar in the server's mods or plugins folder.</summary>
public sealed partial class InstalledModViewModel(InstalledMod mod, ModUpdate? update = null) : ObservableObject
{
    public InstalledMod Mod { get; } = mod;

    /// <summary>A newer release on Modrinth for this server, or null.</summary>
    public ModUpdate? Update { get; } = update;

    public bool HasUpdate => Update is not null;

    public string Name => Mod.DisplayName;

    /// <summary>The file name under a Modrinth title; "Added by hand" for jars WaNut did not install.</summary>
    public string DetailText => Mod.Title is null ? Text.Get("Mods_ByHand") : Mod.FileName;

    public string NewVersionText => Update is null ? string.Empty : string.Format(Text.Get("Mods_NewVersion"), Update.Version.VersionNumber);

    public string AccessibleName => HasUpdate ? $"{Name}, {DetailText}, {NewVersionText}" : $"{Name}, {DetailText}";

    public string RemoveLabel => string.Format(Text.Get("Mods_RemoveLabel"), Name);

    public string UpdateLabel => string.Format(Text.Get("Mods_UpdateLabel"), Name);

    public override string ToString() => AccessibleName;
}
