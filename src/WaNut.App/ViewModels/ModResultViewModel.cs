using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Mods;

namespace WaNut.App.ViewModels;

/// <summary>One Modrinth search result on the Mods tab.</summary>
public sealed partial class ModResultViewModel(ModSearchHit hit, bool isInstalled) : ObservableObject
{
    public ModSearchHit Hit { get; } = hit;

    /// <summary>Installed projects show "Installed" instead of an Install button.</summary>
    [ObservableProperty]
    private bool _isInstalled = isInstalled;

    public string Title => Hit.Title;

    public string Description => Hit.Description;

    public string DetailText => Hit.Author.Length == 0
        ? string.Format(Text.Get("Mods_Downloads"), FormatCount(Hit.Downloads))
        : string.Format(Text.Get("Mods_By"), Hit.Author) + " · " + string.Format(Text.Get("Mods_Downloads"), FormatCount(Hit.Downloads));

    public string AccessibleName => $"{Title}, {DetailText}";

    public string InstallLabel => string.Format(Text.Get("Mods_InstallLabel"), Title);

    public string PageLabel => string.Format(Text.Get("Mods_PageLabel"), Title);

    public override string ToString() => AccessibleName;

    /// <summary>"950", "12.3K", "4.1M": the short form Modrinth shows.</summary>
    internal static string FormatCount(long count) => count switch
    {
        >= 1_000_000 => (count / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (count / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K",
        _ => count.ToString(CultureInfo.InvariantCulture),
    };
}
