using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WaNut.App.Resources;
using WaNut.Core.Hosting;
using Forms = System.Windows.Forms;

namespace WaNut.App.Services;

/// <summary>
/// WaNut's icon in the notification area while its window is hidden and a server keeps running. The icon's dot
/// follows the server: green online, amber starting or saving, gray stopped. It also carries WaNut's Windows
/// notifications, showing just for as long as the notification does when the window is open.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    // Windows cuts tooltips longer than this.
    private const int MaxTooltip = 63;

    private readonly ServerController _controller;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<Color, Icon> _icons = [];
    private Forms.NotifyIcon? _icon;
    private Forms.ToolStripMenuItem? _stopItem;
    private bool _toldOnce;

    // Shown only to carry a notification while the window is open; hidden again once the notification goes.
    private bool _noticeOnly;

    public TrayIcon(ServerController controller)
    {
        _controller = controller;
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>"Open WaNut" was picked, or the icon double-clicked.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>"Exit WaNut" was picked: stop the server and close.</summary>
    public event EventHandler? ExitRequested;

    public void Show()
    {
        var icon = EnsureIcon();
        _noticeOnly = false;
        Refresh();
        icon.Visible = true;
        _timer.Start();
        if (!_toldOnce)
        {
            // Once per run, so the user knows where WaNut went.
            _toldOnce = true;
            icon.ShowBalloonTip(5000, Text.Get("App_Title"), Text.Get("Tray_Balloon"), Forms.ToolTipIcon.None);
        }
    }

    /// <summary>A Windows notification from WaNut. Clicking it opens WaNut.</summary>
    public void Notify(string title, string message)
    {
        var icon = EnsureIcon();
        if (!icon.Visible)
        {
            _noticeOnly = true;
            Refresh();
            icon.Visible = true;
        }

        icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.None);
    }

    public void Hide()
    {
        _noticeOnly = false;
        _timer.Stop();
        if (_icon is not null)
        {
            _icon.Visible = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _icon?.Dispose();
        foreach (var icon in _icons.Values)
        {
            DestroyIcon(icon.Handle);
            icon.Dispose();
        }
    }

    private Forms.NotifyIcon EnsureIcon()
    {
        if (_icon is null)
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(Text.Get("Tray_Open"), null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
            _stopItem = new Forms.ToolStripMenuItem(Text.Get("Tray_StopServer"), null, async (_, _) =>
            {
                if (_controller.Running is { } running)
                {
                    await _controller.StopAsync(running);
                }
            });
            menu.Items.Add(_stopItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Text.Get("Tray_Exit"), null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
            _icon = new Forms.NotifyIcon { ContextMenuStrip = menu };
            _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
            _icon.BalloonTipClicked += (_, _) =>
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
                EndNotice();
            };
            _icon.BalloonTipClosed += (_, _) => EndNotice();
        }

        return _icon;
    }

    private void EndNotice()
    {
        if (_noticeOnly)
        {
            Hide();
        }
    }

    private void Refresh()
    {
        if (_icon is null)
        {
            return;
        }

        var server = _controller.Running;
        var status = server?.Status ?? ServerStatus.Stopped;
        var text = server is null ? Text.Get("App_Title") : $"{Text.Get("App_Title")} · {server.Name}: {server.StatusText}";
        _icon.Text = text.Length > MaxTooltip ? text[..(MaxTooltip - 1)] + "…" : text;
        _icon.Icon = IconFor(status switch
        {
            ServerStatus.Online => Color.FromArgb(0x3C, 0xA3, 0x4F),
            ServerStatus.Starting or ServerStatus.Stopping => Color.FromArgb(0xE0, 0x9B, 0x2D),
            _ => Color.FromArgb(0x9A, 0x94, 0x8C),
        });
        _stopItem!.Enabled = status is ServerStatus.Starting or ServerStatus.Online;
    }

    /// <summary>The WaNut icon with a status dot in the corner, drawn once per color.</summary>
    private Icon IconFor(Color dot)
    {
        if (_icons.TryGetValue(dot, out var cached))
        {
            return cached;
        }

        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/wanut.ico"));
        using var stream = resource.Stream;
        using var baseIcon = new Icon(stream, 32, 32);
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.DrawIcon(baseIcon, new Rectangle(0, 0, 32, 32));
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var outline = new SolidBrush(Color.White);
            using var fill = new SolidBrush(dot);
            graphics.FillEllipse(outline, 18, 18, 14, 14);
            graphics.FillEllipse(fill, 20, 20, 10, 10);
        }

        var icon = Icon.FromHandle(bitmap.GetHicon());
        _icons[dot] = icon;
        return icon;
    }

    // Icon.FromHandle does not own the handle GetHicon made, so it is freed by hand.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
