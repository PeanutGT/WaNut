using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Mods;

namespace WaNut.App.ViewModels;

/// <summary>One Modrinth search result on the Mods tab.</summary>
public sealed partial class ModResultViewModel(ModSearchHit hit, bool isInstalled, bool showServerSide = true) : ObservableObject
{
    private ImageSource? _icon;
    private bool _iconLoaded;

    public ModSearchHit Hit { get; } = hit;

    /// <summary>Installed projects show "Installed" instead of an Install button.</summary>
    [ObservableProperty]
    private bool _isInstalled = isInstalled;

    public string Title => Hit.Title;

    public string Description => Hit.Description;

    /// <summary>The project's icon, fetched by WPF on first use; blank when it has none or it does not load.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _icon = LoadIcon(Hit.IconUrl);
            }

            return _icon;
        }
    }

    public string DetailText
    {
        get
        {
            var parts = new List<string>();
            if (Hit.Author.Length > 0)
            {
                parts.Add(string.Format(Text.Get("Mods_By"), Hit.Author));
            }

            parts.Add(string.Format(Text.Get("Mods_Downloads"), FormatCount(Hit.Downloads)));
            if (Hit.Updated is { } updated)
            {
                parts.Add(UpdatedText(updated, DateTimeOffset.Now));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>"Needed on the server" or "Optional on the server" for mods; empty for plugins, which always run there.</summary>
    public string ServerSideText => !showServerSide ? string.Empty : Hit.ServerSide switch
    {
        "required" => Text.Get("Mods_ServerRequired"),
        "optional" => Text.Get("Mods_ServerOptional"),
        _ => string.Empty,
    };

    public bool HasServerSideText => ServerSideText.Length > 0;

    public string AccessibleName => HasServerSideText ? $"{Title}, {ServerSideText}, {DetailText}" : $"{Title}, {DetailText}";

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

    /// <summary>"updated today", "updated 5 days ago", "updated 3 months ago", "updated 2 years ago".</summary>
    internal static string UpdatedText(DateTimeOffset updated, DateTimeOffset now)
    {
        var days = (int)Math.Floor((now - updated).TotalDays);
        return days switch
        {
            < 1 => Text.Get("Mods_UpdatedToday"),
            < 31 => string.Format(Text.Get("Mods_UpdatedDays"), days),
            < 365 => string.Format(Text.Get("Mods_UpdatedMonths"), days / 30),
            _ => string.Format(Text.Get("Mods_UpdatedYears"), days / 365),
        };
    }

    private static BitmapImage? LoadIcon(Uri? url)
    {
        if (url is null)
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = url;
            image.DecodePixelWidth = 80;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            return image;
        }
        catch (Exception e) when (e is NotSupportedException or System.IO.IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
