using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaNut.Core.Instances;

namespace WaNut.App.Services;

/// <summary>Turns any picture into the 64x64 server-icon.png Minecraft wants, and reads it back for WaNut's list.</summary>
public static class ServerIconMaker
{
    /// <summary>
    /// Crops the picture to a centered square and scales it to 64x64. Small pixel art keeps sharp pixels; photos
    /// are smoothed. Throws <see cref="NotSupportedException"/> or <see cref="FileFormatException"/> for a file that
    /// is not a picture.
    /// </summary>
    public static void Save(string picture, string instanceDir)
    {
        var source = LoadUnlocked(picture);
        var side = Math.Min(source.PixelWidth, source.PixelHeight);
        var square = new CroppedBitmap(source, new Int32Rect((source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, side <= ServerIcon.Size / 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(square, new Rect(0, 0, ServerIcon.Size, ServerIcon.Size));
        }

        var icon = new RenderTargetBitmap(ServerIcon.Size, ServerIcon.Size, 96, 96, PixelFormats.Pbgra32);
        icon.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(icon));
        var target = ServerIcon.PathIn(instanceDir);
        var temp = target + ".tmp";
        using (var stream = File.Create(temp))
        {
            encoder.Save(stream);
        }

        File.Move(temp, target, overwrite: true);
    }

    /// <summary>The server's icon, or null when it has none or the file can't be read.</summary>
    public static ImageSource? Load(string instanceDir)
    {
        var path = ServerIcon.PathIn(instanceDir);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return LoadUnlocked(path);
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the whole file at once, so it isn't kept open (a later replace or delete would fail).</summary>
    private static BitmapSource LoadUnlocked(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
