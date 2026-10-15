using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WaNut.App.Controls;

/// <summary>
/// One of the 32x32 peanut illustrations in Assets/Scenes, scaled with nearest neighbor so the pixels stay sharp.
/// Animated scenes loop through 4 frames of 0.25 s; the type icons and no-backups have a single frame.
/// </summary>
public sealed class PixelScene : Image
{
    private const int FrameCount = 4;

    private static readonly HashSet<string> StaticScenes = ["no-backups", "type-vanilla", "type-paper", "type-fabric"];

    private static readonly Dictionary<string, BitmapImage[]> Cache = [];

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(string), typeof(PixelScene), new PropertyMetadata(null, (d, _) => ((PixelScene)d).Reload()));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(0.25) };
    private BitmapImage[] _frames = [];
    private int _frame;

    public PixelScene()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        Stretch = Stretch.Uniform;
        _timer.Tick += (_, _) => ShowFrame(_frame + 1);
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    /// <summary>The scene name, e.g. "no-servers" or "online".</summary>
    public string? Scene
    {
        get => (string?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    private void Reload()
    {
        _frames = Scene is { Length: > 0 } scene ? Frames(scene) : [];
        ShowFrame(0);
        UpdateTimer();
    }

    private void ShowFrame(int frame)
    {
        _frame = _frames.Length == 0 ? 0 : frame % _frames.Length;
        Source = _frames.Length == 0 ? null : _frames[_frame];
    }

    private void UpdateTimer()
    {
        if (_frames.Length > 1 && IsLoaded && IsVisible)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private static BitmapImage[] Frames(string scene)
    {
        if (!Cache.TryGetValue(scene, out var frames))
        {
            var count = StaticScenes.Contains(scene) ? 1 : FrameCount;
            frames = Enumerable.Range(1, count).Select(k => Load(k == 1 ? $"{scene}-32" : $"{scene}-f{k}-32")).ToArray();
            Cache[scene] = frames;
        }

        return frames;
    }

    private static BitmapImage Load(string file)
    {
        var image = new BitmapImage(new Uri($"pack://application:,,,/Assets/Scenes/{file}.png"));
        image.Freeze();
        return image;
    }
}
