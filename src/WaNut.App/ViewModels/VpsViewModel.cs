using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Remote;

namespace WaNut.App.ViewModels;

/// <summary>
/// Adds or edits one of the user's own VPS hosts. Typed secrets stay in memory until Save; "Test connection" uses
/// them directly, so cancelling never leaves a half-saved password behind.
/// </summary>
public sealed partial class VpsViewModel(SecretStore secrets, VpsTester tester, IDialogService dialogs, IClipboardService clipboard)
    : ObservableObject
{
    private VpsProfile _profile = new() { Login = VpsLogin.GeneratedKey };
    private SshKeyPair? _newKey;
    private CancellationTokenSource? _test;

    /// <summary>Raised with true after Save, false after Cancel.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>The saved profile after Save; null until then.</summary>
    public VpsProfile? Saved { get; private set; }

    public bool IsNew { get; private set; } = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _port = VpsProfile.DefaultPort.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _user = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesGeneratedKey), nameof(UsesKeyFile), nameof(UsesPassword))]
    private VpsLogin _login = VpsLogin.GeneratedKey;

    public bool UsesGeneratedKey
    {
        get => Login == VpsLogin.GeneratedKey;
        set
        {
            if (value)
            {
                Login = VpsLogin.GeneratedKey;
            }
        }
    }

    public bool UsesKeyFile
    {
        get => Login == VpsLogin.KeyFile;
        set
        {
            if (value)
            {
                Login = VpsLogin.KeyFile;
            }
        }
    }

    public bool UsesPassword
    {
        get => Login == VpsLogin.Password;
        set
        {
            if (value)
            {
                Login = VpsLogin.Password;
            }
        }
    }

    [ObservableProperty]
    private string _keyFile = string.Empty;

    /// <summary>Typed in a PasswordBox (which can't bind); empty keeps the saved one.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Typed in a PasswordBox; empty keeps the saved one, or means the key has none.</summary>
    public string Passphrase { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _hasSavedPassword;

    [ObservableProperty]
    private bool _hasSavedPassphrase;

    /// <summary>The line for ~/.ssh/authorized_keys, once WaNut has made a key for this VPS.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPublicKey))]
    [NotifyCanExecuteChangedFor(nameof(CopyAuthorizeCommandCommand))]
    private string? _publicKey;

    public bool HasPublicKey => PublicKey is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand), nameof(SaveCommand))]
    private bool _isTesting;

    [ObservableProperty]
    private string? _testText;

    [ObservableProperty]
    private string? _testProblems;

    [ObservableProperty]
    private string? _errorText;

    public void Initialize(VpsProfile? existing)
    {
        if (existing is null)
        {
            MakeNewKey();
            return;
        }

        IsNew = false;
        _profile = existing;
        Name = existing.Name;
        Host = existing.Host;
        Port = existing.Port.ToString(CultureInfo.InvariantCulture);
        User = existing.User;
        KeyFile = existing.KeyFile ?? string.Empty;
        HasSavedPassword = secrets.Has(existing.Id, SecretKind.Password);
        HasSavedPassphrase = secrets.Has(existing.Id, SecretKind.KeyPassphrase);

        // Set before Login, so choosing the generated key doesn't make a new one.
        PublicKey = existing.Login == VpsLogin.GeneratedKey && secrets.Has(existing.Id, SecretKind.GeneratedKey) ? existing.PublicKey : null;
        Login = existing.Login;
        OnLoginChanged(Login);
    }

    partial void OnLoginChanged(VpsLogin value)
    {
        if (value == VpsLogin.GeneratedKey && PublicKey is null)
        {
            MakeNewKey();
        }
    }

    /// <summary>Replaces the key, for when the old one leaked or was lost. The VPS must be told the new one.</summary>
    [RelayCommand]
    private void MakeNewKey()
    {
        _newKey = SshKeyPair.Generate($"WaNut-{Environment.MachineName}");
        PublicKey = _newKey.PublicKey;
    }

    [RelayCommand(CanExecute = nameof(HasPublicKey))]
    private void CopyAuthorizeCommand()
    {
        ErrorText = clipboard.TrySetText(SshKeyPair.AuthorizeCommand(PublicKey!)) ? null : Text.Get("Vps_CopyFailed");
        TestText = ErrorText is null ? Text.Get("Vps_CommandCopied") : null;
    }

    [RelayCommand]
    private void BrowseKeyFile()
    {
        if (dialogs.PickKeyFile() is { } file)
        {
            KeyFile = file;
        }
    }

    private bool IsIdle() => !IsTesting;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task TestAsync()
    {
        if (Draft() is not { } draft)
        {
            return;
        }

        IsTesting = true;
        TestText = Text.Get("Vps_Testing");
        TestProblems = null;
        ErrorText = null;
        _test = new CancellationTokenSource();
        try
        {
            while (true)
            {
                try
                {
                    var info = await tester.TestAsync(draft, TypedSecret, _test.Token);
                    _profile = _profile with { Host = draft.Host, Port = draft.Port, HostKey = draft.HostKey };
                    TestText = Describe(info);
                    TestProblems = DescribeProblems(info);
                    return;
                }
                catch (HostKeyException e) when (AskToTrust(e))
                {
                    draft = draft with { HostKey = e.Fingerprint };
                }
            }
        }
        catch (HostKeyException e)
        {
            TestText = null;
            ErrorText = string.Format(Text.Get(e.Changed ? "Vps_HostKeyChangedRefused" : "Vps_HostKeyNotTrusted"), e.Fingerprint);
        }
        catch (VpsLoginException e)
        {
            TestText = null;
            ErrorText = Text.Get($"Vps_Login{e.Failure}");
        }
        catch (VpsUnreachableException e)
        {
            TestText = null;
            ErrorText = string.Format(Text.Get("Vps_Unreachable"), e.InnerException?.Message);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or OperationCanceledException)
        {
            TestText = null;
            ErrorText = string.Format(Text.Get("Vps_TestFailed"), e.Message);
        }
        finally
        {
            _test.Dispose();
            _test = null;
            IsTesting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Save()
    {
        if (Draft() is not { } draft)
        {
            return;
        }

        try
        {
            SaveSecrets(draft);
        }
        catch (IOException e)
        {
            ErrorText = string.Format(Text.Get("Settings_SaveFailed"), e.Message);
            return;
        }

        Saved = draft;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        _test?.Cancel();
        CloseRequested?.Invoke(false);
    }

    /// <summary>The profile as typed, or null (with <see cref="ErrorText"/> set) when a field is wrong.</summary>
    private VpsProfile? Draft()
    {
        var draft = _profile with
        {
            Name = Name.Trim(),
            Host = Host.Trim(),
            Port = int.TryParse(Port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : 0,
            User = User.Trim(),
            Login = Login,
            KeyFile = Login == VpsLogin.KeyFile ? KeyFile.Trim() : null,
            PublicKey = Login == VpsLogin.GeneratedKey ? PublicKey : null,
        };

        // A host typed differently is probably another machine, so its key must be confirmed again.
        if (!string.Equals(draft.Host, _profile.Host, StringComparison.OrdinalIgnoreCase) || draft.Port != _profile.Port)
        {
            draft = draft with { HostKey = null };
        }

        if (draft.Validate() is { } problem)
        {
            ErrorText = Text.Get($"Vps_Problem{problem}");
            return null;
        }

        if (draft.Login == VpsLogin.Password && Password.Length == 0 && !HasSavedPassword)
        {
            ErrorText = Text.Get("Vps_ProblemNoPassword");
            return null;
        }

        ErrorText = null;
        return draft;
    }

    private string? TypedSecret(SecretKind kind) => kind switch
    {
        SecretKind.Password when Password.Length > 0 => Password,
        SecretKind.KeyPassphrase when Passphrase.Length > 0 => Passphrase,
        SecretKind.GeneratedKey when _newKey is not null => _newKey.PrivateKey,
        _ => null,
    };

    private void SaveSecrets(VpsProfile draft)
    {
        // Only the secret for the chosen way of signing in is kept; the others would just sit there.
        if (draft.Login == VpsLogin.Password && Password.Length > 0)
        {
            secrets.Save(draft.Id, SecretKind.Password, Password);
        }
        else if (draft.Login != VpsLogin.Password)
        {
            secrets.Delete(draft.Id, SecretKind.Password);
        }

        if (draft.Login == VpsLogin.KeyFile && Passphrase.Length > 0)
        {
            secrets.Save(draft.Id, SecretKind.KeyPassphrase, Passphrase);
        }
        else if (draft.Login != VpsLogin.KeyFile)
        {
            secrets.Delete(draft.Id, SecretKind.KeyPassphrase);
        }

        if (draft.Login == VpsLogin.GeneratedKey && _newKey is not null)
        {
            secrets.Save(draft.Id, SecretKind.GeneratedKey, _newKey.PrivateKey);
        }
        else if (draft.Login != VpsLogin.GeneratedKey)
        {
            secrets.Delete(draft.Id, SecretKind.GeneratedKey);
        }
    }

    private bool AskToTrust(HostKeyException e) =>
        dialogs.Confirm(string.Format(Text.Get(e.Changed ? "Vps_HostKeyChanged" : "Vps_HostKeyFirst"), e.Fingerprint));

    private static string Describe(HostInfo info)
    {
        var name = info.DistroName.Length > 0 ? info.DistroName : info.Os;
        var arch = info.Architecture switch
        {
            HostArchitecture.X64 => "x64",
            HostArchitecture.Arm64 => "ARM64",
            _ => info.Machine,
        };
        return string.Format(
            Text.Get("Vps_TestOk"),
            name,
            arch,
            (info.MemoryBytes / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.CurrentCulture),
            (info.FreeDiskBytes / (1024.0 * 1024 * 1024)).ToString("0", CultureInfo.CurrentCulture));
    }

    private static string? DescribeProblems(HostInfo info)
    {
        var lines = info.Problems.Select(p => "• " + Text.Get($"Vps_HostProblem{p}")).ToList();
        if (!info.IsSupportedDistro && !info.Problems.Contains(HostProblem.NotLinux))
        {
            lines.Add("• " + Text.Get("Vps_UntestedDistro"));
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }
}
