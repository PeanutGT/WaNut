using System.ComponentModel;
using System.IO;
using System.Net.Http;
using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.Core.Backups;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;

namespace WaNut.App.Services;

/// <summary>
/// Starts and stops servers for the screens: one server at a time, and never a forced close without asking.
/// </summary>
public sealed class ServerController(
    ServerHost host, ServerLaunchPlanner planner, IDialogService dialogs, BackupStore backups, AppSettingsViewModel app, FirewallService firewall,
    PortForwardService portForwards,
    TunnelService tunnels,
    DesktopNotifier notifier)
{
    /// <summary>How long a server gets to save and exit before the user is asked about forcing it.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The server whose process is running, or null when none is.</summary>
    public ServerViewModel? Running { get; private set; }

    /// <summary>The server being backed up or getting its Java before it starts, or null.</summary>
    public ServerViewModel? Preparing { get; private set; }

    /// <summary>Raised when <see cref="Preparing"/> changes, so every server can grey out its Start button.</summary>
    public event EventHandler? PreparingChanged;

    public async Task StartAsync(ServerViewModel server)
    {
        // One server at a time (decision 5) includes one getting ready: its backup and Java download come first.
        if (Preparing is not null)
        {
            return;
        }

        if (Running is { } other && other != server && other.IsRunning)
        {
            if (!dialogs.Confirm(string.Format(Text.Get("Server_StopOtherConfirm"), other.Name, server.Name)))
            {
                return;
            }

            await StopAsync(other);
        }

        server.BeginPreparing();
        SetPreparing(server);
        try
        {
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

            // Before java.exe first listens, so Windows never shows its own firewall prompt behind WaNut's window.
            if (await firewall.AskOnceAsync(launch.FileName, server.Metadata.JavaMajor) is { } change)
            {
                server.Connect.OnFirewallAsked(change);
            }

            host.Start(server.Id, launch, server.Attach);
            Running = server;
        }
        catch (ServerStartException e)
        {
            server.ShowError(Text.Get($"Server_Error_{e.Problem}"));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or DownloadVerificationException or Win32Exception or InvalidDataException)
        {
            server.ShowError(string.Format(Text.Get("Server_StartFailed"), e.Message));
        }
        finally
        {
            SetPreparing(null);
            server.EndPreparing();
        }
    }

    private void SetPreparing(ServerViewModel? server)
    {
        Preparing = server;
        PreparingChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Stops the running server, if any, and removes its router rule and ends the playit agent before WaNut closes.</summary>
    public async Task ShutdownAsync()
    {
        if (Running is { IsRunning: true } running)
        {
            await StopAsync(running);
        }

        await portForwards.CloseAnyAsync();
        tunnels.StopAny();
    }

    public bool HasRunningServer => Running is { IsRunning: true };

    /// <summary>A deleted server must not be kept as the last one that ran.</summary>
    public void Forget(ServerViewModel server)
    {
        if (Running == server)
        {
            Running = null;
        }
    }
}
