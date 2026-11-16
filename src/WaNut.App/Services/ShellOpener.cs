using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace WaNut.App.Services;

/// <summary>Opens a web page or a folder with whatever Windows uses for it.</summary>
public static class ShellOpener
{
    /// <summary>False when Windows could not open it, e.g. no default browser is set. Never throws.</summary>
    public static bool TryOpen(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryOpen(Uri page) => TryOpen(page.AbsoluteUri);
}
