using System.Runtime.InteropServices;
using System.Windows;

namespace WaNut.App.Services;

public sealed class ClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        try
        {
            // Clipboard managers and remote desktop often hold the clipboard for a moment, so try a few times.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (ExternalException) when (attempt < 5)
                {
                    Thread.Sleep(100);
                }
            }
        }
        catch (ExternalException)
        {
            return false;
        }
    }
}
