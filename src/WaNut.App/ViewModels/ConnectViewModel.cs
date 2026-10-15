using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Network;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Connect tab: the addresses friends use to join (one per network adapter, with Copy) and whether Windows
/// Firewall lets them through.
/// </summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    public static readonly Uri RadminVpnSite = new("https://www.radmin-vpn.com/");

    private readonly ServerViewModel _server;
    private readonly FirewallService _firewall;
    private readonly IClipboardService _clipboard;
    private int _firewallCheck;

    public ConnectViewModel(ServerViewModel server, FirewallService firewall, IClipboardService clipboard)
    {
        _server = server;
        _firewall = firewall;
        _clipboard = clipboard;
    }

    public ObservableCollection<ConnectAddressViewModel> Addresses { get; } = [];

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

    [ObservableProperty]
    private string? _errorText;

    public bool HasAddresses => Addresses.Count > 0;

    public bool ShowFirewallOk => FirewallState == Core.Network.FirewallState.Allowed;

    public bool ShowFirewallProblem => FirewallState is Core.Network.FirewallState.NotAllowed or Core.Network.FirewallState.Blocked;

    public string? FirewallText => IsCheckingFirewall
        ? Text.Get("Connect_FirewallChecking")
        : FirewallState is { } state ? Text.Get($"Connect_Firewall_{state}") : null;

    public bool CanAllowFirewall => !IsCheckingFirewall && ShowFirewallProblem;

    /// <summary>The tab was opened: adapters come and go (Radmin VPN connects, Wi-Fi changes), and the port may have changed.</summary>
    public void OnShown()
    {
        CopiedText = null;
        ErrorText = null;
        Refresh();
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
    private static void OpenRadminSite() => Process.Start(new ProcessStartInfo(RadminVpnSite.AbsoluteUri) { UseShellExecute = true });

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

    private void LoadAddresses()
    {
        var port = ServerSettings.DefaultPort;
        try
        {
            port = ServerSettingsStore.Load(_server.InstanceDir).Port;
        }
        catch (IOException)
        {
            // Show the default port rather than no addresses at all.
        }

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
