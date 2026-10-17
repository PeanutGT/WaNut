using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Hosting;
using WaNut.Core.Network;
using WaNut.Core.Settings;
using WaNut.Core.Sharing;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Connect tab: the addresses friends use to join (one per network adapter, with Copy) and whether Windows
/// Firewall lets them through, and the port on the router (UPnP) for friends over the internet.
/// </summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    // Only these fixed sites can be opened from the guide.
    private static readonly Dictionary<string, Uri> ToolSites = new()
    {
        [nameof(AdapterKind.RadminVpn)] = new("https://www.radmin-vpn.com/"),
        [nameof(AdapterKind.ZeroTier)] = new("https://www.zerotier.com/"),
        [nameof(AdapterKind.Tailscale)] = new("https://tailscale.com/"),
        [nameof(AdapterKind.Hamachi)] = new("https://vpn.net/"),
    };

    private readonly ServerViewModel _server;
    private readonly FirewallService _firewall;
    private readonly PortForwardService _portForwards;
    private readonly IClipboardService _clipboard;
    private int _firewallCheck;

    // The router's internet address while the port is forwarded, listed with the other addresses.
    private IPAddress? _routerAddress;
    private int _routerPort;

    public ConnectViewModel(
        ServerViewModel server, FirewallService firewall, PortForwardService portForwards, IClipboardService clipboard, AppSettingsViewModel app)
    {
        App = app;
        _server = server;
        _firewall = firewall;
        _portForwards = portForwards;
        _clipboard = clipboard;
        OpenRouterPort = server.Metadata.OpenRouterPort;
        RouterNote = OpenRouterPort ? Text.Get("Connect_RouterWhenStarted") : null;
    }

    public ObservableCollection<ConnectAddressViewModel> Addresses { get; } = [];

    /// <summary>For the invite note, which every server shares.</summary>
    public AppSettingsViewModel App { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirewallText), nameof(ShowFirewallOk), nameof(ShowFirewallProblem), nameof(CanAllowFirewall))]
    [NotifyCanExecuteChangedFor(nameof(AllowFirewallCommand))]
    private FirewallState? _firewallState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirewallText), nameof(CanAllowFirewall))]
    [NotifyCanExecuteChangedFor(nameof(AllowFirewallCommand))]
    private bool _isCheckingFirewall;

    [ObservableProperty]
    private string? _firewallMessage;

    [ObservableProperty]
    private string? _copiedText;

    /// <summary>Forward the port on the router while this server runs (saved in instance.json).</summary>
    [ObservableProperty]
    private bool _openRouterPort;

    [ObservableProperty]
    private bool _isAskingRouter;

    /// <summary>How the router answered when all is well, or what happens next.</summary>
    [ObservableProperty]
    private string? _routerNote;

    /// <summary>Why friends still can't come in over the internet, with what to do.</summary>
    [ObservableProperty]
    private string? _routerProblem;

    [ObservableProperty]
    private string? _errorText;

    public bool HasAddresses => Addresses.Count > 0;

    public bool ShowFirewallOk => FirewallState == Core.Network.FirewallState.Allowed;

    public bool ShowFirewallProblem => FirewallState is Core.Network.FirewallState.NotAllowed or Core.Network.FirewallState.Blocked;

    public string? FirewallText => IsCheckingFirewall
        ? Text.Get("Connect_FirewallChecking")
        : FirewallState is { } state ? Text.Get($"Connect_Firewall_{state}") : null;

    public bool CanAllowFirewall => !IsCheckingFirewall && ShowFirewallProblem;

    /// <summary>
    /// The tab was opened: adapters come and go (Radmin VPN connects, Wi-Fi changes), and the port may have changed.
    /// The firewall note stays, since it may answer the question asked when the server started.
    /// </summary>
    public void OnShown()
    {
        CopiedText = null;
        ErrorText = null;
        LoadAddresses();
        _ = CheckFirewallAsync(keepMessage: true);
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadAddresses();
        _ = CheckFirewallAsync();
    }

    [RelayCommand]
    private void Copy(ConnectAddressViewModel? address)
    {
        if (address is null)
        {
            return;
        }

        if (_clipboard.TrySetText(address.Endpoint))
        {
            ErrorText = null;
            CopiedText = string.Format(Text.Get("Connect_Copied"), address.Endpoint);
        }
        else
        {
            CopiedText = null;
            ErrorText = Text.Get("Connect_CopyFailed");
        }
    }

    /// <summary>Copies a ready-to-paste invite: name, version, every address, the host's note and, for a modded server, the mods.</summary>
    [RelayCommand]
    private void CopyInvite()
    {
        LoadAddresses();
        var mods = Core.Instances.ServerCreator.LoadsMods(_server.Metadata.Type)
            ? _server.Mods.Installed.Select(m => m.Name).ToList()
            : [];
        var content = new InviteCardContent(
            _server.Name, _server.TypeAndVersionText, Addresses.Select(a => (a.Title, a.Endpoint)).ToList(), App.InviteNote, mods);
        var labels = new InviteCardLabels(
            Text.Get("Invite_Title"), Text.Get("Invite_Version"), Text.Get("Invite_Addresses"), Text.Get("Invite_NoAddress"),
            Text.Get("Invite_Note"), string.Format(Text.Get("Invite_Mods"), Text.Get($"CreateServer_Type{_server.Metadata.Type}")));

        if (_clipboard.TrySetText(InviteCard.Compose(content, labels)))
        {
            ErrorText = null;
            CopiedText = Text.Get("Invite_Copied");
        }
        else
        {
            CopiedText = null;
            ErrorText = Text.Get("Connect_CopyFailed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanAllowFirewall))]
    private async Task AllowFirewallAsync()
    {
        var major = _server.Metadata.JavaMajor;
        if (_firewall.FindJavaExe(major) is not { } javaExe)
        {
            FirewallMessage = Text.Get("Connect_FirewallNoJava");
            return;
        }

        IsCheckingFirewall = true;
        try
        {
            ShowFirewallChange(await _firewall.AllowAsync(javaExe, major), javaExe);
        }
        finally
        {
            IsCheckingFirewall = false;
        }

        await CheckFirewallAsync(keepMessage: true);
    }

    [RelayCommand]
    private static void OpenSite(string? tool)
    {
        if (tool is not null && ToolSites.TryGetValue(tool, out var site))
        {
            Process.Start(new ProcessStartInfo(site.AbsoluteUri) { UseShellExecute = true });
        }
    }

    partial void OnOpenRouterPortChanged(bool value)
    {
        if (value == _server.Metadata.OpenRouterPort)
        {
            return;
        }

        _server.UpdateMetadata(_server.Metadata with { OpenRouterPort = value });
        if (value && _server.IsRunning)
        {
            _ = OpenRouterPortNowAsync();
        }
        else
        {
            _ = CloseRouterPortAsync();
        }
    }

    /// <summary>The port is forwarded while the server runs, and the rule is removed once it stops.</summary>
    internal void OnServerStatusChanged(ServerStatus status)
    {
        if (status == ServerStatus.Starting && OpenRouterPort)
        {
            _ = OpenRouterPortNowAsync();
        }
        else if (status == ServerStatus.Stopped)
        {
            _ = CloseRouterPortAsync();
        }
    }

    private async Task OpenRouterPortNowAsync()
    {
        var port = LoadPort();
        RouterNote = null;
        RouterProblem = null;
        IsAskingRouter = true;
        PortForwardResult result;
        try
        {
            result = await _portForwards.OpenAsync(_server.Id, port);
        }
        finally
        {
            IsAskingRouter = false;
        }

        // Turned off, or stopped, while the router was answering.
        if (!OpenRouterPort || !_server.IsRunning)
        {
            await CloseRouterPortAsync();
            return;
        }

        if (result.Outcome == PortForwardOutcome.Opened)
        {
            _routerAddress = result.PublicAddress;
            _routerPort = port;
            RouterNote = string.Format(Text.Get(_routerAddress is null ? "Connect_RouterOpenedNoAddress" : "Connect_RouterOpened"), port);
            LoadAddresses();
        }
        else
        {
            RouterProblem = string.Format(Text.Get($"Connect_Router_{result.Outcome}"), port);
        }
    }

    private async Task CloseRouterPortAsync()
    {
        RouterProblem = null;
        RouterNote = OpenRouterPort ? Text.Get("Connect_RouterWhenStarted") : null;
        if (_routerAddress is not null)
        {
            _routerAddress = null;
            LoadAddresses();
        }

        await _portForwards.CloseAsync(_server.Id);
    }

    /// <summary>The start of a server asked about the firewall; show the outcome here too.</summary>
    internal void OnFirewallAsked(FirewallChange change)
    {
        if (_firewall.FindJavaExe(_server.Metadata.JavaMajor) is { } javaExe)
        {
            ShowFirewallChange(change, javaExe);
        }

        _ = CheckFirewallAsync(keepMessage: true);
    }

    private void ShowFirewallChange(FirewallChange change, string javaExe) => FirewallMessage = change switch
    {
        FirewallChange.Done => null,
        FirewallChange.Declined => Text.Get("Connect_FirewallDeclined"),
        _ => string.Format(Text.Get("Connect_FirewallFailed"), javaExe),
    };

    private int LoadPort()
    {
        try
        {
            return ServerSettingsStore.Load(_server.InstanceDir).Port;
        }
        catch (IOException)
        {
            // Show the default port rather than no addresses at all.
            return ServerSettings.DefaultPort;
        }
    }

    private void LoadAddresses()
    {
        var port = LoadPort();
        Addresses.Clear();
        try
        {
            foreach (var address in LocalAddresses.Get())
            {
                Addresses.Add(new ConnectAddressViewModel(address, port));
            }
        }
        catch (NetworkInformationException e)
        {
            ErrorText = string.Format(Text.Get("Connect_ListFailed"), e.Message);
        }

        if (_routerAddress is { } internet)
        {
            Addresses.Add(new ConnectAddressViewModel(new LocalAddress(string.Empty, AdapterKind.Internet, internet), _routerPort));
        }

        OnPropertyChanged(nameof(HasAddresses));
    }

    private async Task CheckFirewallAsync(bool keepMessage = false)
    {
        // Several checks can overlap (tab shown, Refresh, a start); only the newest one updates the screen.
        var check = ++_firewallCheck;
        if (!keepMessage)
        {
            FirewallMessage = null;
        }

        if (_firewall.FindJavaExe(_server.Metadata.JavaMajor) is not { } javaExe)
        {
            FirewallState = null;
            FirewallMessage = Text.Get("Connect_FirewallNoJava");
            return;
        }

        IsCheckingFirewall = true;
        try
        {
            var state = await _firewall.GetStateAsync(javaExe);
            if (check == _firewallCheck)
            {
                FirewallState = state;
            }
        }
        finally
        {
            if (check == _firewallCheck)
            {
                IsCheckingFirewall = false;
            }
        }
    }
}
