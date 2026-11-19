using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WaNut.App.Services;

/// <summary>
/// Over Remote Desktop the GPU path of WPF can leave the window content blank white (title bar still draws).
/// In a remote session, or when the user sets WANUT_SOFTWARE_RENDER=1, drawing is done on the CPU instead.
/// </summary>
public static class RenderFallback
{
    public static bool ShouldUseSoftware() =>
        SystemParameters.IsRemoteSession
        || Environment.GetEnvironmentVariable("WANUT_SOFTWARE_RENDER") == "1";

    /// <summary>Call before the first window is created. Returns true when software rendering was chosen.</summary>
    public static bool Apply()
    {
        if (!ShouldUseSoftware())
        {
            return false;
        }

        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            return true;
        }
        catch (Exception)
        {
            // Setting the mode must never stop WaNut from opening.
            return false;
        }
    }
}
