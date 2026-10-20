using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaNut.Core.Players;

namespace WaNut.App.Services;

/// <summary>
/// Players' faces for the Players tab: the 8x8 face of their skin with the hat layer on top, as Minecraft draws it in
/// the player list. Each name is looked up once per run; the skins themselves are cached on disk by <see cref="PlayerSkins"/>.
/// </summary>
public sealed class PlayerFaces(PlayerSkins skins)
{
    private static readonly Int32Rect FaceArea = new(8, 8, 8, 8);
    private static readonly Int32Rect HatArea = new(40, 8, 8, 8);

    // A few lookups at a time, so a long list of players does not flood Mojang.
    private readonly SemaphoreSlim _lookups = new(4);
    private readonly Dictionary<string, Task<ImageSource?>> _faces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The player's face, or null when they have no custom skin or it could not be loaded.</summary>
    public Task<ImageSource?> GetAsync(string name)
    {
        if (!_faces.TryGetValue(name, out var face))
        {
            face = LoadAsync(name);
            _faces[name] = face;
        }

        return face;
    }

    private async Task<ImageSource?> LoadAsync(string name)
    {
        await _lookups.WaitAsync();
        try
        {
            return await skins.GetSkinFileAsync(name) is { } file ? Crop(file) : null;
        }
        finally
        {
            _lookups.Release();
        }
    }

    private static ImageSource? Crop(string file)
    {
        try
        {
            var skin = new BitmapImage();
            skin.BeginInit();
            skin.CacheOption = BitmapCacheOption.OnLoad;
            skin.UriSource = new Uri(file);
            skin.EndInit();
            skin.Freeze();

            // Classic skins are 64x32 and current ones 64x64; both keep the face and hat in the top rows.
            if (skin.PixelWidth != 64 || skin.PixelHeight is not (32 or 64))
            {
                return null;
            }

            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
            var bounds = new Rect(0, 0, 8, 8);
            group.Children.Add(new ImageDrawing(new CroppedBitmap(skin, FaceArea), bounds));
            group.Children.Add(new ImageDrawing(new CroppedBitmap(skin, HatArea), bounds));
            var face = new DrawingImage(group);
            face.Freeze();
            return face;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
