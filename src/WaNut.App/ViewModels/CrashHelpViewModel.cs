using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Crashes;
using WaNut.Core.Downloads;
using WaNut.Core.Mods;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>
/// The card above the tabs after a crash or failed start: what went wrong in plain words and, when WaNut can, a button
/// that fixes it (turn a mod off, install a missing one, switch to a free port, or open the tab to change).
/// </summary>
public sealed partial class CrashHelpViewModel(ServerViewModel server, IDialogService dialogs, IClipboardService clipboard, ModLibrary mods) : ObservableObject
{
    /// <summary>How many of the last console lines "Copy messages" puts on the clipboard.</summary>
    private const int CopiedLines = 80;

    private IReadOnlyList<string> _lines = [];
    private IReadOnlyList<ModJar> _jars = [];
    private int? _freePort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(Title), nameof(Text), nameof(FixText), nameof(HasFix))]
    private CrashDiagnosis? _diagnosis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(Title), nameof(Text))]
    private int? _exitCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    private string? _reportPath;

    /// <summary>Set once a fix ran: replaces the explanation and hides the fix button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(HasFix))]
    private string? _doneText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFix))]
    [NotifyCanExecuteChangedFor(nameof(FixCommand))]
    private bool _isFixing;

    public bool IsShown => Diagnosis is not null || ExitCode is not null;

    public bool HasReport => ReportPath is not null;

    public string Title => Diagnosis is { } d
        ? string.Format(Resources.Text.Get($"Crash_{d.Cause}_Title"), ModNames(d.ModIds))
        : Resources.Text.Get("Crash_Unknown_Title");

    public string Text => DoneText ?? (Diagnosis is { } d ? Explain(d) : string.Format(Resources.Text.Get("Crash_Unknown_Text"), ExitCode));

    /// <summary>The fix button's label, or null when there is nothing WaNut can do by itself.</summary>
    public string? FixText => Diagnosis switch
    {
        null => null,
        { Cause: CrashCause.PortInUse } => _freePort is { } port ? string.Format(Resources.Text.Get("Crash_Fix_UsePort"), port) : null,
        { Cause: CrashCause.MissingMod } d when Platform is not null => string.Format(Resources.Text.Get("Crash_Fix_Install"), d.ModIds[0]),
        { Cause: CrashCause.DuplicateMod } => DuplicatesToTurnOff().Count > 0 ? Resources.Text.Get("Crash_Fix_TurnOffOlder") : OpenModsFolderText,
        { Cause: CrashCause.IncompatibleMod or CrashCause.ClientOnlyMod or CrashCause.ModCrashed or CrashCause.WrongJava } d =>
            JarsOf(d.ModIds).Count > 0 ? string.Format(Resources.Text.Get("Crash_Fix_TurnOff"), ModNames(d.ModIds)) : OpenModsFolderText,
        { Cause: CrashCause.OutOfMemory or CrashCause.MemoryTooHigh } => Resources.Text.Get("Crash_Fix_Memory"),
        { Cause: CrashCause.BrokenWorld } => Resources.Text.Get("Crash_Fix_Backups"),
        _ => null,
    };

    public bool HasFix => FixText is not null && DoneText is null && !IsFixing;

    private ModPlatform? Platform => ModPlatform.For(server.Metadata.Type);

    private string? OpenModsFolderText => Platform is null ? null : Resources.Text.Get("Crash_Fix_OpenMods");

    /// <summary>Shows the card for a run that ended badly. <paramref name="diagnosis"/> is null when the cause is unknown.</summary>
    internal void Show(CrashDiagnosis? diagnosis, int exitCode, IReadOnlyList<string> lines, string? reportPath)
    {
        _lines = lines;
        _jars = Platform is { } platform && diagnosis?.Cause is CrashCause.DuplicateMod or CrashCause.IncompatibleMod or CrashCause.ClientOnlyMod
                or CrashCause.ModCrashed or CrashCause.WrongJava or CrashCause.MissingMod
            ? ModJars.Read(platform.FolderIn(server.InstanceDir))
            : [];
        _freePort = diagnosis?.Cause == CrashCause.PortInUse
            ? CrashFixes.NextFreePort(ServerSettingsStore.Load(server.InstanceDir).Port, CrashFixes.IsPortFree)
            : null;
        DoneText = null;
        ReportPath = reportPath;
        ExitCode = exitCode;
        Diagnosis = diagnosis;
        OnPropertyChanged(nameof(FixText));
    }

    [RelayCommand]
    internal void Hide()
    {
        Diagnosis = null;
        ExitCode = null;
        ReportPath = null;
        DoneText = null;
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task FixAsync()
    {
        if (Diagnosis is not { } d || !server.CanDelete)
        {
            return;
        }

        IsFixing = true;
        try
        {
            switch (d.Cause)
            {
                case CrashCause.PortInUse when _freePort is { } port:
                    UsePort(port);
                    break;
                case CrashCause.MissingMod:
                    await InstallAsync(d.ModIds[0]);
                    break;
                case CrashCause.DuplicateMod when DuplicatesToTurnOff() is { Count: > 0 } older:
                    TurnOff(older);
                    break;
                case CrashCause.IncompatibleMod or CrashCause.ClientOnlyMod or CrashCause.ModCrashed or CrashCause.WrongJava when JarsOf(d.ModIds) is { Count: > 0 } jars:
                    TurnOff(jars);
                    break;
                case CrashCause.OutOfMemory or CrashCause.MemoryTooHigh:
                    server.RequestTab(ServerTab.Server);
                    break;
                case CrashCause.BrokenWorld:
                    server.RequestTab(ServerTab.Backups);
                    break;
                default:
                    OpenModsFolder();
                    break;
            }
        }
        finally
        {
            IsFixing = false;
        }
    }

    private bool CanFix() => !IsFixing;

    [RelayCommand]
    private void OpenReport()
    {
        if (ReportPath is not null)
        {
            Open(ReportPath);
        }
    }

    [RelayCommand]
    private void CopyLines()
    {
        var text = string.Join(Environment.NewLine, _lines.TakeLast(CopiedLines));
        DoneText = clipboard.TrySetText(text) ? Resources.Text.Get("Crash_Copied") : Resources.Text.Get("Crash_CopyFailed");
    }

    private string Explain(CrashDiagnosis d)
    {
        var names = ModNames(d.ModIds);
        var game = server.Metadata.MinecraftVersion;
        var memory = server.Metadata.MemoryMb / 1024.0;
        return d.Cause switch
        {
            CrashCause.PortInUse => string.Format(Resources.Text.Get(_freePort is null ? "Crash_PortInUse_TextNoFree" : "Crash_PortInUse_Text"), d.Detail, _freePort),
            CrashCause.DuplicateMod => string.Format(Resources.Text.Get("Crash_DuplicateMod_Text"), names, string.Join(", ", DuplicateFiles())),
            CrashCause.MissingMod => string.Format(
                Resources.Text.Get(Platform is null ? "Crash_MissingMod_TextNoInstall" : "Crash_MissingMod_Text"),
                names,
                d.Detail is null ? Resources.Text.Get("Crash_AMod") : ModNames([d.Detail])),
            CrashCause.IncompatibleMod => d.Detail?.ToLowerInvariant() switch
            {
                "minecraft" => string.Format(Resources.Text.Get("Crash_IncompatibleMod_TextMinecraft"), names, game),
                "fabricloader" or "forge" or "neoforge" => string.Format(Resources.Text.Get("Crash_IncompatibleMod_TextLoader"), names, server.Metadata.Type),
                null => string.Format(Resources.Text.Get("Crash_IncompatibleMod_TextConflict"), names),
                _ => string.Format(Resources.Text.Get("Crash_IncompatibleMod_TextOther"), names, ModNames([d.Detail])),
            },
            CrashCause.ClientOnlyMod => d.ModIds.Count > 0
                ? string.Format(Resources.Text.Get("Crash_ClientOnlyMod_Text"), names)
                : Resources.Text.Get("Crash_ClientOnlyMod_TextUnknown"),
            CrashCause.OutOfMemory => string.Format(Resources.Text.Get("Crash_OutOfMemory_Text"), memory),
            CrashCause.MemoryTooHigh => string.Format(Resources.Text.Get("Crash_MemoryTooHigh_Text"), memory),
            CrashCause.WrongJava => d.ModIds.Count > 0
                ? string.Format(Resources.Text.Get("Crash_WrongJava_TextMod"), names, d.Detail ?? "?", server.Metadata.JavaMajor, game)
                : string.Format(Resources.Text.Get("Crash_WrongJava_Text"), d.Detail ?? "?", server.Metadata.JavaMajor),
            CrashCause.BrokenWorld => Resources.Text.Get("Crash_BrokenWorld_Text"),
            CrashCause.ModCrashed => string.Format(Resources.Text.Get("Crash_ModCrashed_Text"), names),
            _ => string.Empty,
        };
    }

    /// <summary>Display names from the jars where WaNut can read them, the ids otherwise.</summary>
    private string ModNames(IReadOnlyList<string> ids) =>
        string.Join(", ", ids.Select(id => ModJars.Find(_jars, id).FirstOrDefault()?.Name ?? id));

    private List<ModJar> JarsOf(IEnumerable<string> ids) => ids.SelectMany(id => ModJars.Find(_jars, id)).DistinctBy(j => j.FileName).ToList();

    private IEnumerable<string> DuplicateFiles() =>
        Diagnosis is { Files.Count: > 0 } d ? d.Files : JarsOf(Diagnosis?.ModIds ?? []).Select(j => j.FileName);

    /// <summary>Every copy but the newest, for each duplicated mod.</summary>
    private List<ModJar> DuplicatesToTurnOff()
    {
        if (Diagnosis is not { Cause: CrashCause.DuplicateMod } d)
        {
            return [];
        }

        var files = d.Files.Count > 0 ? d.Files.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        return d.ModIds
            .Select(id => ModJars.Find(_jars, id).Where(j => files is null || files.Contains(j.FileName)).ToList())
            .Where(copies => copies.Count > 1)
            .SelectMany(copies => copies.Where(j => j != ModJars.Newest(copies)))
            .ToList();
    }

    private void UsePort(int port)
    {
        try
        {
            var loaded = ServerSettingsStore.Load(server.InstanceDir);
            ServerSettingsStore.Save(server.InstanceDir, loaded, loaded with { Port = port });
            server.ServerSettings.Reload();
            DoneText = string.Format(Resources.Text.Get("Crash_Done_Port"), port);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DoneText = string.Format(Resources.Text.Get("Crash_FixFailed"), e.Message);
        }
    }

    private void TurnOff(IReadOnlyList<ModJar> jars)
    {
        if (Platform is not { } platform)
        {
            return;
        }

        var names = string.Join(", ", jars.Select(j => j.FileName));
        if (!dialogs.Confirm(string.Format(Resources.Text.Get("Crash_TurnOffConfirm"), names, platform.FolderName + "-disabled")))
        {
            return;
        }

        try
        {
            foreach (var jar in jars)
            {
                mods.Disable(server.InstanceDir, platform, jar.FileName);
            }

            DoneText = string.Format(Resources.Text.Get("Crash_Done_TurnedOff"), names, platform.FolderName + "-disabled");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DoneText = string.Format(Resources.Text.Get("Crash_FixFailed"), e.Message);
        }
        finally
        {
            server.Mods.Reload();
        }
    }

    private async Task InstallAsync(string modId)
    {
        if (Platform is not { } platform)
        {
            return;
        }

        var project = CrashFixes.ModrinthProjectFor(modId);
        if (!dialogs.Confirm(string.Format(Resources.Text.Get("Crash_InstallConfirm"), modId)))
        {
            return;
        }

        DoneText = string.Format(Resources.Text.Get("Crash_Installing"), modId);
        try
        {
            var title = await mods.Modrinth.GetProjectTitleAsync(project);
            ModInstallResult result;
            try
            {
                result = await mods.InstallAsync(server.InstanceDir, platform, server.Metadata.MinecraftVersion, project, title);
            }
            catch (IncompatibleModsException e)
            {
                if (!dialogs.Confirm(string.Format(Resources.Text.Get("Mods_IncompatibleConfirm"), title, string.Join(", ", e.Installed))))
                {
                    DoneText = string.Format(Resources.Text.Get("Mods_IncompatibleSkipped"), title);
                    return;
                }

                result = await mods.InstallAsync(server.InstanceDir, platform, server.Metadata.MinecraftVersion, project, title, allowIncompatible: true);
            }

            DoneText = string.Format(Resources.Text.Get("Crash_Done_Installed"), result.Installed.Count > 0 ? string.Join(", ", result.Installed) : title);
        }
        catch (Exception e) when (e is NoCompatibleVersionException or HttpRequestException or TaskCanceledException or JsonException
            or InvalidDataException or DownloadVerificationException or IOException or UnauthorizedAccessException)
        {
            // Not every mod id is a Modrinth slug: let the user pick from a search instead.
            server.Mods.Query = modId;
            server.Mods.SearchCommand.Execute(null);
            server.RequestTab(ServerTab.Mods);
            DoneText = string.Format(Resources.Text.Get("Crash_InstallFailed"), modId);
        }
        finally
        {
            server.Mods.Reload();
        }
    }

    private void OpenModsFolder()
    {
        if (Platform is { } platform)
        {
            var dir = platform.FolderIn(server.InstanceDir);
            Directory.CreateDirectory(dir);
            Open(dir);
        }
    }

    private void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            DoneText = string.Format(Resources.Text.Get("Crash_FixFailed"), e.Message);
        }
    }
}
