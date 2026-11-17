using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WaNut.Core.Bridge;

/// <summary>
/// The running bridge answers on a local pipe, one per server: send <see cref="StatusRequest"/> or
/// <see cref="StopRequest"/> as one line, read one line of <see cref="BridgeStatus"/> JSON back. Only the same user
/// can connect (on Linux too, where the pipe is a Unix socket). The app asks it on this PC; on a VPS the bridge
/// executable asks it for WaNut (<c>--status</c>).
/// </summary>
public static class BridgeStatusPipe
{
    public const string StatusRequest = "status";

    public const string StopRequest = "stop";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The pipe name for a server; ids are folder names, so anything else is replaced to keep it one word.</summary>
    public static string PipeName(string serverId) =>
        "WaNut.Bridge." + new string(serverId.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());

    public static string Serialize(BridgeStatus status) => JsonSerializer.Serialize(status, BridgeStatusJsonContext.Default.BridgeStatus);

    /// <exception cref="JsonException">The text is not a status.</exception>
    public static BridgeStatus Deserialize(string json) =>
        JsonSerializer.Deserialize(json, BridgeStatusJsonContext.Default.BridgeStatus) ?? throw new JsonException("The bridge status is empty.");

    /// <summary>Asks the bridge of <paramref name="serverId"/>; null when no bridge answers in time.</summary>
    public static async Task<BridgeStatus?> AskAsync(string serverId, string request = StatusRequest, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName(serverId), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), deadline.Token).ConfigureAwait(false);
            await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            return line is null ? null : Deserialize(line);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or TimeoutException)
        {
            return null;
        }
    }
}
