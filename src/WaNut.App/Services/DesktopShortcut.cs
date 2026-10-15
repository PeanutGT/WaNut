using System.IO;

namespace WaNut.App.Services;

/// <summary>
/// WaNut's shortcut on the desktop. The installer no longer makes one, so the first-run setup can ask instead.
/// </summary>
public static class DesktopShortcut
{
    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WaNut.lnk");

    /// <summary>Points the shortcut at the running WaNut.exe, which stays in the same place across updates.</summary>
    public static void Create()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("WaNut.exe path is unknown.");
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is missing.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var shortcut = shell.CreateShortcut(ShortcutPath);
        shortcut.TargetPath = exe;
        shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
        shortcut.IconLocation = exe + ",0";
        shortcut.Save();
    }

    /// <summary>Removes the shortcut, including the one WaNut 0.1.0's installer made.</summary>
    public static void Remove()
    {
        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }
}
