using System.ComponentModel;
using System.IO;
using System.Net.Http;
using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;

namespace WaNut.App.Services;

/// <summary>
/// Starts and stops servers for the screens: one server at a time, and never a forced close without asking.
/// </summary>
public sealed class ServerController(ServerHost host, ServerLaunchPlanner planner, IDialogService dialogs)
{
    /// <summary>How long a server gets to save and exit before the user is asked about forcing it.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The server whose process is running, or null when none is.</summary>
    public ServerViewModel? Running { get; private set; }

    public async Task StartAsync(ServerViewModel server)
    {
        if (Running is { } other && other != server && other.IsRunning)
        {
            if (!dialogs.Confirm(string.Format(Text.Get("Server_StopOtherConfirm"), other.Name, server.Name)))
            {
                return;
            }

            await StopAsync(other);
        }

        server.BeginPreparing();
        try
        {
            var progress = new Progress<DownloadProgress>(server.ReportJavaDownload);
            var launch = await planner.PrepareAsync(server.Metadata, progress);
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
            server.EndPreparing();
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

    /// <summary>Stops the running server, if any, before WaNut closes.</summary>
    public Task ShutdownAsync() => Running is { IsRunning: true } running ? StopAsync(running) : Task.CompletedTask;

    public bool HasRunningServer => Running is { IsRunning: true };
}
