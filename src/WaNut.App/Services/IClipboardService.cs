namespace WaNut.App.Services;

public interface IClipboardService
{
    /// <summary>Returns false when another program holds the clipboard.</summary>
    bool TrySetText(string text);
}
