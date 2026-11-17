using System.Globalization;
using System.Text;
using Renci.SshNet;
using WaNut.Core.Remote.Run;

namespace WaNut.Core.Remote.Sync;

/// <summary>
/// Lists a server folder on the VPS with one <c>find</c> command: one round trip instead of several per folder
/// over SFTP. Needs GNU find (<c>-printf</c>); callers fall back to SFTP when it isn't there.
/// </summary>
public static class RemoteListing
{
    /// <summary>Type, size, modified time and path (relative, '/'), separated by tabs; entries end with NUL so any file name is safe.</summary>
    public static string Command(string root) =>
        "sh -c " + RemoteShell.Quote(
            $"cd {RemoteShell.Home(root)} 2>/dev/null || exit 0; " +
            "find . -mindepth 1 \\( -type f -o -type d \\) -printf '%y\\t%s\\t%T@\\t%P\\0'");

    /// <summary>Entries from <see cref="Command"/>'s output. A name Windows can't hold as one path part is left out.</summary>
    public static IReadOnlyList<SyncEntry> Parse(string output)
    {
        var entries = new List<SyncEntry>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = record.Split('\t', 4);
            if (parts.Length != 4 || parts[3].Length == 0)
            {
                throw new FormatException("Unexpected listing line from the VPS.");
            }

            if (parts[3].Contains('\\') || parts[3].Split('/').Any(p => p.Length == 0))
            {
                continue;
            }

            var seconds = double.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture);
            var modified = DateTime.UnixEpoch.AddSeconds(seconds);
            var isDirectory = parts[0] == "d";
            entries.Add(new SyncEntry(parts[3], isDirectory, isDirectory ? 0 : long.Parse(parts[1], CultureInfo.InvariantCulture), modified));
        }

        return entries;
    }

    public static async Task<IReadOnlyList<SyncEntry>> ListAsync(SshClient ssh, string root, CancellationToken cancellationToken = default)
    {
        using var command = ssh.CreateCommand(Command(root), Encoding.UTF8);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (command.ExitStatus is not 0)
        {
            throw new InvalidOperationException($"Listing on the VPS failed ({command.ExitStatus}): {command.Error.Trim()}");
        }

        return Parse(command.Result);
    }
}
