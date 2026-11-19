using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Hotplug;

namespace WaNut.App.ViewModels;

/// <summary>A row of the live data pack panel: one pack in the world's datapacks folder on the VPS.</summary>
public sealed partial class LiveDatapackViewModel(LiveDatapack pack, string removeLabel) : ObservableObject
{
    public LiveDatapack Pack { get; } = pack;

    public string Name => Pack.FileName;

    public string StateText => Text.Get(Pack.Enabled switch
    {
        true => "Hotplug_Enabled",
        false => "Hotplug_Disabled",
        null => "Hotplug_StateUnknown",
    });

    public bool IsEnabled => Pack.Enabled == true;

    public string DetailText => string.Join(" · ", new[]
    {
        Pack.IsFolder ? Text.Get("Hotplug_Folder") : Size(Pack.SizeBytes),
        Pack.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        FormatText,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Filled in once the zip's pack.mcmeta was read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText), nameof(AccessibleName))]
    private string? _formatText;

    /// <summary>Folder packs and names WaNut can't pass to /datapack safely are only shown.</summary>
    public bool CanRemove => Pack.CanHotplug;

    public string RemoveLabel { get; } = removeLabel;

    public string AccessibleName => $"{Name}, {StateText}, {DetailText}";

    public override string ToString() => AccessibleName;

    private static string Size(long bytes) => bytes < 1024 * 1024
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} KB", bytes / 1024.0)
        : string.Format(CultureInfo.CurrentCulture, "{0:0.#} MB", bytes / (1024.0 * 1024));
}
