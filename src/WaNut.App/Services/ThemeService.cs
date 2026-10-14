using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace WaNut.App.Services;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Follows the Windows light/dark setting: swaps Themes/Light.xaml and Themes/Dark.xaml in the
/// application resources and colors each window's title bar to match.
/// </summary>
public sealed class ThemeService
{
    /// <summary>"light" or "dark" forces a theme, for testing both without changing the Windows setting.</summary>
    public const string ThemeVariable = "WANUT_THEME";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // DwmSetWindowAttribute values. Caption and text colors need Windows 11; older versions ignore them.
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private readonly Application _app;
    private ResourceDictionary? _current;

    public ThemeService(Application app)
    {
        _app = app;
    }

    public AppTheme Theme { get; private set; }

    /// <summary>Applies the current Windows theme and keeps following it.</summary>
    public void Start()
    {
        Apply(ReadWindowsTheme());

        // Every window, including dialogs opened later, gets a matching title bar once it is shown.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => ColorTitleBar((Window)sender)));

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                _app.Dispatcher.BeginInvoke(() => Apply(ReadWindowsTheme()));
            }
        };
    }

    /// <summary>Windows stores "apps use light theme" as 1 or 0; a missing value means light.</summary>
    public static AppTheme ReadWindowsTheme()
    {
        if (Enum.TryParse<AppTheme>(Environment.GetEnvironmentVariable(ThemeVariable), ignoreCase: true, out var forced))
        {
            return forced;
        }

        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is 0 ? AppTheme.Dark : AppTheme.Light;
    }

    private void Apply(AppTheme theme)
    {
        if (_current is not null && theme == Theme)
        {
            return;
        }

        var next = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{theme}.xaml") };
        var merged = _app.Resources.MergedDictionaries;
        if (_current is null)
        {
            merged.Insert(0, next);
        }
        else
        {
            merged[merged.IndexOf(_current)] = next;
        }

        _current = next;
        Theme = theme;

        foreach (Window window in _app.Windows)
        {
            ColorTitleBar(window);
        }
    }

    private void ColorTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = Theme == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        var caption = ToColorRef((Color)_app.Resources["CaptionColor"]);
        _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));

        var text = ToColorRef((Color)_app.Resources["CaptionTextColor"]);
        _ = DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));
    }

    /// <summary>Win32 COLORREF is 0x00BBGGRR.</summary>
    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
