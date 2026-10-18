using System.Windows;
using WaNut.App.Resources;

namespace WaNut.App.Services;

/// <summary>Tells the user a long job finished when they have switched to another app while it ran.</summary>
public sealed class DesktopNotifier(Func<TrayIcon> tray)
{
    /// <summary>Shows a Windows notification, unless one of WaNut's windows is in front and the user can already see.</summary>
    public void NotifyIfAway(string message)
    {
        if (Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive) == true)
        {
            return;
        }

        tray().Notify(Text.Get("App_Title"), message);
    }
}
