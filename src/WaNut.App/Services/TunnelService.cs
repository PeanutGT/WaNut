using System.Diagnostics;
using System.Reflection;
using WaNut.Core.Downloads;
using WaNut.Core.Tunnels;

namespace WaNut.App.Services;

/// <summary>
/// The playit.gg tunnel (blueprint 2, step 20): approving this PC once in the browser, then running WaNut's own playit
/// agent while a server with "Let friends join over the internet" on is running. One agent carries every tunnel of
/// the account, so it serves all such servers and ends with the last one; every server keeps its own tunnel and address.
/// </summary>
public sealed class TunnelService(PlayitFiles files, PlayitAgentDownloader downloader, PlayitTunnels tunnels) : IDisposable
{
    private readonly PlayitAgentProcess _agent = new(files);
    private readonly HashSet<string> _owners = [];

    public bool IsSetUp => files.HasSecret;

    /// <summary>
    /// Opens playit.gg's approval page in the browser and waits until the user approved it, then keeps the key.
    /// <paramref name="claimUrl"/> gets the page, so the screen can offer to open it again.
    /// </summary>
    public async Task SetUpAsync(Action<Uri> claimUrl, IProgress<PlayitClaimStatus> progress, CancellationToken cancellationToken)
    {
        var code = PlayitApi.NewClaimCode();
        var url = PlayitApi.ClaimUrl(code);
        claimUrl(url);
        OpenInBrowser(url);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var secret = await tunnels.WaitForApprovalAsync(code, $"WaNut {version}", progress, cancellationToken: cancellationToken);
        files.SaveSecret(secret);
    }

    /// <summary>
    /// Makes sure the server's tunnel exists and leads to <paramref name="port"/>, then starts the agent for
    /// <paramref name="owner"/>. A key playit no longer accepts is forgotten, so the next try asks for approval again.
    /// </summary>
    public async Task<PlayitTunnel> StartAsync(
        string owner, string serverName, Guid? tunnelId, int port, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var secret = files.LoadSecret() ?? throw new PlayitException("NotSetUp");
        var agentExe = await downloader.EnsureInstalledAsync(progress, cancellationToken);

        // Started first: playit only makes a new tunnel for an agent that has connected.
        _owners.Add(owner);
        _agent.Start(agentExe);
        try
        {
            return await tunnels.EnsureTunnelAsync(secret, tunnelId, serverName, port, cancellationToken: cancellationToken);
        }
        catch (PlayitException e) when (e.Code is "InvalidAgentKey" or "AgentNotFound")
        {
            Forget();
            throw new PlayitException("NotSetUp");
        }
        catch
        {
            Stop(owner);
            throw;
        }
    }

    /// <summary>Ends the agent once no running server other than <paramref name="owner"/> needs it.</summary>
    public void Stop(string owner)
    {
        if (_owners.Remove(owner) && _owners.Count == 0)
        {
            StopAny();
        }
    }

    /// <summary>Before WaNut closes, or when the key is forgotten.</summary>
    public void StopAny()
    {
        _owners.Clear();
        _agent.Stop();
    }

    /// <summary>"Forget this playit account": ends the agent and deletes its key. The tunnels stay on playit.gg.</summary>
    public void Forget()
    {
        StopAny();
        files.ForgetSecret();
    }

    public static void OpenInBrowser(Uri url) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });

    public void Dispose() => _agent.Dispose();
}
