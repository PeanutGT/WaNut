using System.ComponentModel;
using System.IO;
using System.Net.Http;
using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.Core.Backups;
using WaNut.Core.Crashes;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Network;
using WaNut.Core.Settings;

namespace WaNut.App.Services;

/// <summary>
/// Starts and stops servers for the screens. Several servers may run at once (decision 5), but only one gets ready
/// at a time: the others wait in line. A forced close always asks first.
/// </summary>
public sealed class ServerController(
    ServerHost host, ServerLaunchPlanner planner, IDialogService dialogs, BackupStore backups, AppSettingsViewModel app, FirewallService firewall,
    PortForwardService portForwards,
    TunnelService tunnels,
    DesktopNotifier notifier)
{
    /// <summary>How long a server gets to save and exit before the user is asked about forcing it.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    // One server prepares at a time, so two never download the same Java or ask for the firewall at once.
    private readonly SemaphoreSlim _prepareGate = new(1, 1);
    private readonly Dictionary<ServerViewModel, CancellationTokenSource> _queue = [];
    private readonly List<ServerViewModel> _started = [];

    /// <summary>The servers whose process is running, in the order they were started.</summary>
    public IReadOnlyList<ServerViewModel> RunningServers => [.. _started.Where(s => s.Process is { IsRunning: true })];

    public bool HasRunningServer => _started.Any(s => s.Process is { IsRunning: true });

    /// <summary>The server being backed up or getting its Java before it starts, or null.</summary>
    public ServerViewModel? Preparing { get; private set; }

    /// <summary>True while a server gets ready or waits in line to.</summary>
    public bool IsAnyPreparing => Preparing is not null || _queue.Count > 0;

    /// <param name="automatic">A restart after a crash: no questions, since nobody may be watching.</param>
    public async Task StartAsync(ServerViewModel server, bool automatic = false)
    {
        if (server.IsPreparing || server.IsRunning)
        {
            return;
        }

        server.BeginPreparing();
        if (!await WaitForTurnAsync(server))
        {
            server.EndPreparing();
            return;
        }

        Preparing = server;
        try
        {
            if (!automatic && !ConfirmMemory(server))
            {
                return;
            }

            if (!await BackUpBeforeStartAsync(server))
            {
                return;
            }

            var progress = new Progress<DownloadProgress>(server.ReportJavaDownload);
            var launch = await planner.PrepareAsync(server.Metadata, progress);
            if (server.DownloadedJava)
            {
                notifier.NotifyIfAway(string.Format(Text.Get("Notify_JavaReady"), server.Name));
            }

            if (!UseFreePort(server))
            {
                return;
            }

            // Before java.exe first listens, so Windows never shows its own firewall prompt behind WaNut's window.
            if (await firewall.AskOnceAsync(launch.FileName, server.Metadata.JavaMajor) is { } change)
            {
                server.Connect.OnFirewallAsked(change);
            }

            host.Start(server.Id, launch, server.Attach);
            _started.Remove(server);
            _started.Add(server);
        }
        catch (ServerStartException e)
        {
            server.ShowError(Text.Get($"Server_Error_{e.Problem}"));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or DownloadVerificationException or Win32Exception or InvalidDataException)
        {
            server.ShowError(string.Format(Text.Get("Server_StartFailed"), e.Message));
        }
        finally
        {
            Preparing = null;
            server.EndPreparing();
            _prepareGate.Release();
        }
    }

    /// <summary>Waits while another server gets ready. False when the wait was cancelled with Stop.</summary>
    private async Task<bool> WaitForTurnAsync(ServerViewModel server)
    {
        if (_prepareGate.Wait(0))
        {
            return true;
        }

        using var cancel = new CancellationTokenSource();
        _queue[server] = cancel;
        server.IsQueued = true;
        try
        {
            await _prepareGate.WaitAsync(cancel.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            _queue.Remove(server);
            server.IsQueued = false;
        }
    }

    /// <summary>Warns when the servers together would leave Windows too little memory; the user may start anyway.</summary>
    private bool ConfirmMemory(ServerViewModel server)
    {
        var running = RunningServers.Where(s => s != server).Select(s => s.Metadata.MemoryMb).ToList();
        var total = MemoryLimits.TotalPhysicalMb();
        if (!MemoryLimits.IsTooMuchTogether(server.Metadata.MemoryMb, running, total))
        {
            return true;
        }

        var together = (server.Metadata.MemoryMb + running.Sum()) / 1024.0;
        return dialogs.Confirm(string.Format(Text.Get("Server_MemoryTogetherConfirm"), server.Name, together, total / 1024.0));
    }

    /// <summary>
    /// Keeps the server's port unless another running server or program has it; then the next free one is saved
    /// to server.properties and the console says so. False when no port nearby is free.
    /// </summary>
    private bool UseFreePort(ServerViewModel server)
    {
        var settings = ServerSettingsStore.Load(server.InstanceDir);
        var taken = RunningServers.Where(s => s != server).Select(s => s.RunningPort).ToList();
        if (ServerPorts.Pick(settings.Port, taken, CrashFixes.IsPortFree) is not { } port)
        {
            server.ShowError(string.Format(Text.Get("Server_NoFreePort"), settings.Port));
            return false;
        }

        if (port != settings.Port)
        {
            ServerSettingsStore.Save(server.InstanceDir, settings, settings with { Port = port });
            server.OnPortMoved(settings.Port, port);
        }

        server.RunningPort = port;
        return true;
    }

    /// <summary>
    /// Backs the world up and applies the cleanup rules (decision 7). If the backup fails, the user decides whether
    /// to start anyway. Returns false to cancel the start.
    /// </summary>
    private async Task<bool> BackUpBeforeStartAsync(ServerViewModel server)
    {
        string problem;
        server.Backups.ErrorText = null;
        server.Backups.DoneText = null;
        try
        {
            var backup = await backups.CreateAsync(server.InstanceDir, BackupReason.Start, new Progress<double>(server.ReportBackup));
            if (backup is not null)
            {
                CleanUp(server);
            }

            return true;
        }
        catch (BackupException e)
        {
            problem = Text.Get($"Backups_Problem_{e.Problem}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = e.Message;
        }
        finally
        {
            server.EndBackup();
            server.Backups.Reload();
        }

        return dialogs.Confirm(string.Format(Text.Get("Server_BackupFailedConfirm"), problem));
    }

    private void CleanUp(ServerViewModel server)
    {
        try
        {
            backups.Cleanup(server.InstanceDir, app.BackupRules);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The new backup is safe; an old one that could not be deleted is tried again next time.
            server.Backups.ErrorText = string.Format(Text.Get("Backups_CleanupFailed"), e.Message);
        }
    }

    /// <summary>
    /// Sends "stop" and waits for the world to be saved. If that takes too long, asks the user whether to force it;
    /// "No" keeps waiting. Completes once the server has exited.
    /// </summary>
    public async Task StopAsync(ServerViewModel server)
    {
        if (_queue.TryGetValue(server, out var waiting))
        {
            waiting.Cancel();
            return;
        }

        var process = server.Process;
        if (process is null || !process.IsRunning)
        {
            return;
        }

        // Stopping saves the world, which would change files the backup is still copying.
        await server.Backups.WaitForLiveBackupAsync();
        while (!await process.StopAsync(StopTimeout))
        {
            if (dialogs.Confirm(string.Format(Text.Get("Server_ForceStopConfirm"), server.Name, (int)StopTimeout.TotalSeconds)))
            {
                process.Kill();
                await process.Exited;
                break;
            }
        }
    }

    /// <summary>Stops every running server at once, then removes the router rules and ends the playit agent before WaNut closes.</summary>
    public async Task ShutdownAsync()
    {
        foreach (var waiting in _queue.Values.ToList())
        {
            waiting.Cancel();
        }

        await Task.WhenAll(RunningServers.Select(StopAsync));
        await portForwards.CloseAnyAsync();
        tunnels.StopAny();
    }

    /// <summary>A deleted server must not be kept as one that ran.</summary>
    public void Forget(ServerViewModel server) => _started.Remove(server);
}
