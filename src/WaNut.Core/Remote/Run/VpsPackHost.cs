using System.Globalization;
using System.Security.Cryptography;
using Renci.SshNet;

namespace WaNut.Core.Remote.Run;

/// <summary>What <see cref="VpsPackHost.StartAsync"/> got going on the VPS.</summary>
/// <param name="Url">The address players' games download the merged pack from.</param>
/// <param name="Port">The TCP port the VPS serves it on; it must be open in the VPS's firewall.</param>
public sealed record VpsPackHostStart(Uri Url, int Port);

/// <summary>
/// Serves a server's merged resource pack from its VPS, because players' games download the pack from the URL in
/// server.properties and this PC may be off. The zip goes up over SFTP and a small sh script (<see cref="Script"/>) runs
/// python3 (or BusyBox) as a web server on a free port from <see cref="FirstPort"/>. The script ends by itself when the
/// server's run ends, so nothing is left behind and WaNut closing changes nothing.
/// </summary>
public sealed class VpsPackHost(VpsConnector connector)
{
    /// <summary>The ports tried for the download, in order. Kept apart from the Minecraft ports (25565 and up by one).</summary>
    public const int FirstPort = 25800;

    public const int LastPort = 25899;

    public const string ZipName = "resourcepack.zip";

    public const string ScriptPath = "wanut/bin/wanut-pack-host.sh";

    /// <summary>Gives up waiting for the server to start after this many 5 second looks (10 minutes).</summary>
    private const int WaitLooks = 120;

    public const string Script = """
        #!/bin/sh
        # WaNut serves a merged resource pack: wanut-pack-host.sh <pack folder> <port> <server run folder>
        dir=$1
        port=$2
        run=$3
        echo $$ > "$dir/pid"
        if command -v python3 >/dev/null 2>&1; then
          python3 -m http.server "$port" --bind 0.0.0.0 --directory "$dir/www" >/dev/null 2>&1 &
        elif command -v busybox >/dev/null 2>&1 && busybox httpd --help 2>&1 | grep -q -- ' -f'; then
          busybox httpd -f -p "$port" -h "$dir/www" >/dev/null 2>&1 &
        else
          rm -f "$dir/pid"
          exit 3
        fi
        child=$!
        trap 'kill $child 2>/dev/null; rm -f "$dir/pid"; exit 0' TERM INT
        i=0
        # Only files newer than this script's own pid file belong to this run: the last run's are still lying there.
        while kill -0 $child 2>/dev/null; do
          if [ "$run/exit" -nt "$dir/pid" ]; then break; fi
          if [ "$run/java.pid" -nt "$dir/pid" ]; then
            kill -0 "$(cat "$run/java.pid")" 2>/dev/null || break
          else
            i=$((i+1))
            [ $i -gt WAIT_LOOKS ] && break
          fi
          sleep 5
        done
        kill $child 2>/dev/null
        rm -f "$dir/pid"
        """;

    /// <summary>The pack folder on the VPS for a server, under the SSH user's home.</summary>
    public static string Folder(string serverId) => $"wanut/packhost/{serverId}";

    /// <summary>
    /// Stops a host left from the last run, writes the script, picks the first free port and starts the host detached
    /// from the SSH session. Prints "port &lt;n&gt;" when it listens, "unsupported" when the VPS has neither python3 nor
    /// BusyBox httpd, "failed" otherwise.
    /// </summary>
    public static string StartCommand(string serverId, string serverRoot)
    {
        var dir = RemoteShell.Home(Folder(serverId));
        var script = RemoteShell.Home(ScriptPath);
        var run = RemoteShell.Home($"{serverRoot}/{RunnerScript.RunFolder}");
        return string.Join('\n',
            $"mkdir -p {RemoteShell.Home("wanut/bin")} {dir}/www",
            $"cat > {script} <<'WANUT_SCRIPT_END'",
            Script.Replace("WAIT_LOOKS", WaitLooks.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).ReplaceLineEndings("\n"),
            "WANUT_SCRIPT_END",
            $"dir={dir}",
            "if p=$(cat \"$dir/pid\" 2>/dev/null) && [ -n \"$p\" ] && grep -q wanut-pack-host /proc/\"$p\"/cmdline 2>/dev/null; then kill \"$p\" 2>/dev/null; sleep 1; fi",
            "rm -f \"$dir/pid\"",
            "listening=$( (ss -ltn 2>/dev/null || netstat -ltn 2>/dev/null) | awk '{print $4}')",
            $"port=; p={FirstPort}",
            $"while [ $p -le {LastPort} ]; do if ! printf '%s\\n' \"$listening\" | grep -q \":$p\\$\"; then port=$p; break; fi; p=$((p+1)); done",
            "[ -n \"$port\" ] || { echo failed; exit 0; }",
            $"setsid nohup sh {script} \"$dir\" \"$port\" {run} </dev/null >/dev/null 2>&1 &",
            "sleep 2",
            "if p=$(cat \"$dir/pid\" 2>/dev/null) && kill -0 \"$p\" 2>/dev/null; then echo \"port $port\"",
            "elif ! command -v python3 >/dev/null 2>&1 && ! command -v busybox >/dev/null 2>&1; then echo unsupported",
            "else echo failed; fi");
    }

    /// <summary>Prints the SHA-1 of the zip already on the VPS, or nothing when there is none or no sha1sum.</summary>
    public static string RemoteSha1Command(string serverId) =>
        $"sha1sum {RemoteShell.Home($"{Folder(serverId)}/www/{ZipName}")} 2>/dev/null | cut -d' ' -f1; true";

    /// <summary>The address players use: plain HTTP to the VPS's own host name or address.</summary>
    public static Uri UrlFor(string host, int port)
    {
        var name = host.Trim().Trim('[', ']');
        if (name.Contains(':', StringComparison.Ordinal))
        {
            name = $"[{name}]";
        }

        return new Uri($"http://{name}:{port}/{ZipName}");
    }

    /// <summary>Reads what <see cref="StartCommand"/> printed.</summary>
    public static (bool Started, int Port, bool Unsupported) ParseStart(string output)
    {
        var line = output.Trim().Split('\n')[^1].Trim();
        if (line.StartsWith("port ", StringComparison.Ordinal)
            && int.TryParse(line.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= FirstPort and <= LastPort)
        {
            return (true, port, false);
        }

        return (false, 0, line == "unsupported");
    }

    /// <summary>
    /// Puts <paramref name="zipPath"/> on the VPS (unless that exact file is already there) and starts the host for the
    /// server whose files are at <paramref name="serverRoot"/>. Returns null when the VPS has no web server tool to use.
    /// </summary>
    /// <exception cref="InvalidOperationException">The VPS could not start the host.</exception>
    public async Task<VpsPackHostStart?> StartAsync(VpsProfile vps, string serverId, string serverRoot, string zipPath, CancellationToken cancellationToken = default)
    {
        using var ssh = await connector.ConnectAsync(vps, null, cancellationToken).ConfigureAwait(false);
        var sha1 = await Sha1Async(zipPath, cancellationToken).ConfigureAwait(false);
        var remote = (await VpsConnector.RunAsync(ssh, RemoteSha1Command(serverId), cancellationToken).ConfigureAwait(false)).Trim();
        if (!string.Equals(remote, sha1, StringComparison.OrdinalIgnoreCase))
        {
            using var sftp = await connector.ConnectSftpAsync(vps, cancellationToken).ConfigureAwait(false);
            var home = (await VpsConnector.RunAsync(ssh, "printf %s \"$HOME\"", cancellationToken).ConfigureAwait(false)).Trim();
            var folder = $"{home}/{Folder(serverId)}/www";
            await VpsConnector.RunAsync(ssh, $"mkdir -p {RemoteShell.Quote(folder)}", cancellationToken).ConfigureAwait(false);
            await using var file = File.OpenRead(zipPath);
            await sftp.UploadFileAsync(file, $"{folder}/{ZipName}.part", cancellationToken).ConfigureAwait(false);
            await VpsConnector.RunAsync(ssh, $"mv -f {RemoteShell.Quote($"{folder}/{ZipName}.part")} {RemoteShell.Quote($"{folder}/{ZipName}")}", cancellationToken).ConfigureAwait(false);
        }

        var (started, port, unsupported) = ParseStart(await VpsConnector.RunAsync(ssh, StartCommand(serverId, serverRoot), cancellationToken).ConfigureAwait(false));
        if (started)
        {
            return new VpsPackHostStart(UrlFor(vps.Host, port), port);
        }

        return unsupported ? null : throw new InvalidOperationException("The VPS could not start the resource pack web server.");
    }

    private static async Task<string> Sha1Async(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA1.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
    }
}
