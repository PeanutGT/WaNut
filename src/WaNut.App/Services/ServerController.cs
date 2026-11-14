using System.ComponentModel;
using System.IO;
using System.Net.Http;
using Renci.SshNet.Common;
using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.Core.Backups;
using WaNut.Core.Crashes;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Network;
using WaNut.Core.Remote;
using WaNut.Core.Remote.Run;
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
    DesktopNotifier notifier,
    VpsLaunchPlanner vpsPlanner,
    VpsConnector vpsConnector)
{
    /// <summary>How long a server gets to save and exit before the user is asked about forcing it.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    // One server prepares at a time, so two never download the same Java or ask for the firewall at once.
    private readonly SemaphoreSlim _prepareGate = new(1, 1);
    private readonly Dictionary<ServerViewModel, CancellationTokenSource> _queue = [];
    private readonly List<ServerViewModel> _started = [];

    // Servers whose folder is coming back from the VPS after a run.
    private readonly Dictionary<ServerViewModel, Task> _syncingBack = [];

    /// <summary>The servers whose process is running, in the order they were started.</summary>
    public IReadOnlyList<ServerViewModel> RunningServers => [.. _started.Where(s => s.Process is { IsRunning: true })];

    public bool HasRunningServer => _started.Any(s => s.Process is { IsRunning: true });

    /// <summary>A server's folder is on its way back from a VPS.</summary>
    public bool IsSyncingBack => _syncingBack.Values.Any(t => !t.IsCompleted);

    /// <summary>The running servers on this PC: only these stop when WaNut closes; VPS servers keep running.</summary>
    public IReadOnlyList<ServerViewModel> LocalRunningServers => [.. RunningServers.Where(s => s.Process is not RemoteServer)];

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

        VpsProfile? vps = null;
        if (server.Metadata.VpsId is { } vpsId && (vps = app.VpsList.FirstOrDefault(v => v.Id == vpsId)) is null)
        {
            server.ShowError(Text.Get("Server_VpsMissing"));
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
            if (vps is not null)
            {
                await StartOnVpsAsync(server, vps);
                return;
            }

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
        catch (HostKeyException e)
        {
            server.ShowError(Text.Get(e.Changed ? "Server_VpsHostKeyChanged" : "Server_VpsHostKeyNew"));
        }
        catch (VpsLoginException)
        {
            server.ShowError(Text.Get("Server_VpsLoginFailed"));
        }
        catch (VpsMemoryException e)
        {
            server.ShowError(string.Format(
                Text.Get(e.VpsTooSmall ? "Server_VpsMemoryTooSmall" : "Server_VpsMemoryTooHigh"),
                server.Metadata.MemoryMb / 1024.0,
                e.VpsMemoryMb / 1024.0,
                e.MaxServerMb / 1024.0,
                (MemoryLimits.MinimumMb + MemoryLimits.KeptForVpsSystemMb) / 1024.0));
        }
        catch (Exception e) when (e is VpsUnreachableException or SshException or InvalidOperationException)
        {
            server.ShowError(string.Format(Text.Get("Server_VpsFailed"), e.InnerException?.Message ?? e.Message));
        }
        finally
        {
            Preparing = null;
            server.EndPreparing();
            _prepareGate.Release();
        }
    }

    /// <summary>
    /// The VPS way (plan step 3): if the newest files are still on the VPS, follow the server when it still runs there
    /// or bring its files back first; then back up here, get the VPS ready and upload, and start there. No firewall,
    /// port or memory checks on this PC: none of them apply to the VPS.
    /// </summary>
    private async Task StartOnVpsAsync(ServerViewModel server, VpsProfile vps)
    {
        if (server.Metadata.VpsCopyIsNewer)
        {
            server.VpsText = Text.Get("Status_VpsConnecting");
            var run = await vpsPlanner.CheckRunAsync(server.Metadata, vps);
            if (run.State == VpsRunState.Running)
            {
                Follow(server, vps, run.RunnerPid);
                return;
            }

            server.VpsText = Text.Get("Status_VpsDownloading");
            await vpsPlanner.DownloadAsync(server.Metadata, vps, DownloadProgress(server));
            server.UpdateMetadata(server.Metadata with { VpsCopyIsNewer = false });
            server.VpsText = null;
        }

        if (!await BackUpBeforeStartAsync(server))
        {
            return;
        }

        var steps = new Progress<VpsStep>(step => server.VpsText = Text.Get($"Status_Vps{step}"));
        var upload = new Progress<DownloadProgress>(p => server.VpsText = string.Format(Text.Get("Status_VpsUploadingPercent"), p.Fraction * 100));
        var memoryRead = new Progress<long>(mb => app.RememberVpsMemory(vps.Id, mb));
        var launch = await vpsPlanner.PrepareAsync(server.Metadata, vps, steps, upload, memoryRead);
        server.VpsText = null;

        // From here the VPS copy is the one that counts, until it has come back.
        server.UpdateMetadata(server.Metadata with { VpsCopyIsNewer = true });
        Track(server, vps, host.Start(server.Id, launch, server.Attach, new RemoteServerRuntime(vpsConnector, vps)));
    }

    /// <summary>Follows a server that is already running on the VPS, such as one left running when WaNut closed.</summary>
    private void Follow(ServerViewModel server, VpsProfile vps, int runnerPid) =>
        Track(server, vps, host.Start(server.Id, VpsLaunchPlanner.AttachLaunch(server.Metadata), server.Attach, new RemoteServerRuntime(vpsConnector, vps, runnerPid)));

    private void Track(ServerViewModel server, VpsProfile vps, IRunningServer process)
    {
        server.RunningPort = 0; // The VPS port takes nothing from this PC.
        _started.Remove(server);
        _started.Add(server);
        _syncingBack[server] = SyncBackAfterExitAsync(server, process, vps);
    }

    /// <summary>
    /// For a server whose newest files are on a VPS while WaNut isn't following it (WaNut was closed, or the connection
    /// was lost): follows it again when it still runs there, otherwise brings its files back. Runs when WaNut starts,
    /// and from the "Check the VPS again" button.
    /// </summary>
    public async Task ReconnectAsync(ServerViewModel server)
    {
        if (!server.Metadata.VpsCopyIsNewer || server.IsRunning || server.IsPreparing || _syncingBack.ContainsKey(server))
        {
            return;
        }

        if (app.VpsList.FirstOrDefault(v => v.Id == server.Metadata.VpsId) is not { } vps)
        {
            server.ShowError(Text.Get("Server_VpsMissing"));
            return;
        }

        server.ErrorText = null;
        server.BeginSyncBack(Text.Get("Status_VpsChecking"));
        VpsRun run;
        try
        {
            run = await vpsPlanner.CheckRunAsync(server.Metadata, vps);
        }
        catch (Exception e) when (IsVpsProblem(e))
        {
            server.ShowError(string.Format(Text.Get("Server_VpsCheckFailed"), VpsMessage(e)));
            return;
        }
        finally
        {
            server.EndSyncBack();
        }

        if (run.State == VpsRunState.Running)
        {
            Follow(server, vps, run.RunnerPid);
            return;
        }

        var back = SyncBackAsync(server, vps);
        _syncingBack[server] = back;
        await back;
    }

    /// <summary>Once the run on the VPS ended, brings the server's folder back here. A failure keeps it on the VPS until the next try.</summary>
    private async Task SyncBackAfterExitAsync(ServerViewModel server, IRunningServer process, VpsProfile vps)
    {
        await process.Exited;
        if (process is RemoteServer { ConnectionLost: true } or RemoteServer { Detached: true })
        {
            _syncingBack.Remove(server);
            return;
        }

        await SyncBackAsync(server, vps);
    }

    /// <summary>Downloads the folder, then backs it up here, the same as before a start (plan step 4).</summary>
    private async Task SyncBackAsync(ServerViewModel server, VpsProfile vps)
    {
        server.BeginSyncBack(Text.Get("Status_VpsDownloading"));
        try
        {
            await vpsPlanner.DownloadAsync(server.Metadata, vps, DownloadProgress(server));
            server.UpdateMetadata(server.Metadata with { VpsCopyIsNewer = false });
            server.VpsText = null;
            await BackUpAfterVpsAsync(server);
        }
        catch (Exception e) when (IsVpsProblem(e))
        {
            server.ShowError(string.Format(Text.Get("Server_VpsSyncBackFailed"), VpsMessage(e)));
        }
        finally
        {
            server.EndSyncBack();
            server.ReloadFiles();
            _syncingBack.Remove(server);
        }
    }

    /// <summary>The world as it came back from the VPS. A failure only shows on the Backups tab: the files are here now.</summary>
    private async Task BackUpAfterVpsAsync(ServerViewModel server)
    {
        try
        {
            if (await backups.CreateAsync(server.InstanceDir, BackupReason.Vps, new Progress<double>(server.ReportBackup)) is not null)
            {
                CleanUp(server);
            }
        }
        catch (Exception e) when (e is BackupException or IOException or UnauthorizedAccessException)
        {
            server.Backups.ErrorText = string.Format(Text.Get("Backups_AfterVpsFailed"), e is BackupException b ? Text.Get($"Backups_Problem_{b.Problem}") : e.Message);
        }
        finally
        {
            server.EndBackup();
            server.Backups.Reload();
        }
    }

    private static Progress<DownloadProgress> DownloadProgress(ServerViewModel server) =>
        new(p => server.VpsText = string.Format(Text.Get("Status_VpsDownloadingPercent"), p.Fraction * 100));

    private static bool IsVpsProblem(Exception e) =>
        e is IOException or UnauthorizedAccessException or SshException or VpsUnreachableException or VpsLoginException or HostKeyException or InvalidOperationException;

    private static string VpsMessage(Exception e) => e switch
    {
        HostKeyException h => Text.Get(h.Changed ? "Server_VpsHostKeyChanged" : "Server_VpsHostKeyNew"),
        VpsLoginException => Text.Get("Server_VpsLoginFailed"),
        _ => e.InnerException?.Message ?? e.Message,
    };

    /// <summary>Completes once the server's folder is back from the VPS, or right away when nothing is coming back.</summary>
    public Task WaitForSyncBackAsync(ServerViewModel server) =>
        _syncingBack.GetValueOrDefault(server) ?? Task.CompletedTask;

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
        var running = RunningServers.Where(s => s != server && s.Metadata.VpsId is null).Select(s => s.Metadata.MemoryMb).ToList();
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

    /// <summary>
    /// Before WaNut closes: stops every server on this PC at once, lets go of the VPS servers (they keep running there
    /// and WaNut follows them again on its next start, plan decision 5A), waits for folders coming back, then removes
    /// the router rules and ends the playit agent.
    /// </summary>
    public async Task ShutdownAsync()
    {
        foreach (var waiting in _queue.Values.ToList())
        {
            waiting.Cancel();
        }

        // One already stopping on the VPS finishes and comes back; any other is left running there.
        foreach (var server in RunningServers)
        {
            if (server.Process is RemoteServer remote && remote.Status != ServerStatus.Stopping)
            {
                remote.Detach();
            }
        }

        await Task.WhenAll(LocalRunningServers.Select(StopAsync));
        await Task.WhenAll(_syncingBack.Values.ToList());
        await portForwards.CloseAnyAsync();
        tunnels.StopAny();
    }

    /// <summary>A deleted server must not be kept as one that ran.</summary>
    public void Forget(ServerViewModel server) => _started.Remove(server);
}
