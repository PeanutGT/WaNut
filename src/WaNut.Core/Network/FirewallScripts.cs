using System.Globalization;
using System.Text;

namespace WaNut.Core.Network;

/// <summary>
/// The PowerShell that reads and changes the firewall rules. Paths are passed inside single-quoted strings and the
/// whole script travels as -EncodedCommand, so no path (spaces, quotes, Chinese user names) can break out of it.
/// </summary>
public static class FirewallScripts
{
    public const string DoneMarker = "WANUT-OK";

    // NetSecurity's enum values: Action Allow = 2, Block = 4; Profile Any = 0, Private = 2, Public = 4.
    private const int ActionAllow = 2;
    private const int ActionBlock = 4;
    private const int ProfilePrivate = 2;
    private const int ProfilePublic = 4;

    /// <summary>Lists the enabled inbound rules for <paramref name="program"/> as "rule &lt;action&gt; &lt;profile&gt;" lines.</summary>
    public static string ReadScript(string program) => $$"""
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        $program = {{Quote(program)}}
        Get-NetFirewallApplicationFilter -Program $program -ErrorAction SilentlyContinue |
            Get-NetFirewallRule |
            Where-Object { $_.Direction -eq 'Inbound' -and $_.Enabled -eq 'True' } |
            ForEach-Object { 'rule {0} {1}' -f [int]$_.Action, [int]$_.Profile }
        '{{DoneMarker}}'
        """;

    /// <summary>
    /// Removes inbound block rules for exactly this program (left behind when Windows' own prompt was cancelled,
    /// and they win over any allow rule), then adds one allow rule for every network profile.
    /// </summary>
    public static string AllowScript(string program, string ruleName) => $$"""
        $ErrorActionPreference = 'Stop'
        $program = {{Quote(program)}}
        Get-NetFirewallApplicationFilter -Program $program -ErrorAction SilentlyContinue |
            Get-NetFirewallRule |
            Where-Object { $_.Direction -eq 'Inbound' -and $_.Action -eq 'Block' } |
            Remove-NetFirewallRule
        New-NetFirewallRule -DisplayName {{Quote(ruleName)}} -Description 'Lets friends join Minecraft servers started by WaNut.' -Direction Inbound -Action Allow -Program $program -Profile Any | Out-Null
        """;

    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    /// <summary>Reads <see cref="ReadScript"/>'s output. Returns <see cref="FirewallState.Unknown"/> if it didn't finish.</summary>
    public static FirewallState ParseState(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!lines.Contains(DoneMarker))
        {
            return FirewallState.Unknown;
        }

        var covered = 0;
        foreach (var line in lines)
        {
            var parts = line.Split(' ');
            if (parts is not ["rule", var actionText, var profileText]
                || !int.TryParse(actionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var action)
                || !int.TryParse(profileText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var profile))
            {
                continue;
            }

            if (action == ActionBlock)
            {
                return FirewallState.Blocked;
            }

            if (action == ActionAllow)
            {
                covered |= profile == 0 ? ProfilePrivate | ProfilePublic : profile;
            }
        }

        return (covered & (ProfilePrivate | ProfilePublic)) == (ProfilePrivate | ProfilePublic) ? FirewallState.Allowed : FirewallState.NotAllowed;
    }

    /// <summary>
    /// A PowerShell single-quoted string: nothing inside is expanded, and a quote is written twice. PowerShell also
    /// reads the curly quotes ‘ ’ ‚ ‛ as single quotes, so those are doubled too.
    /// </summary>
    public static string Quote(string value)
    {
        var quoted = new StringBuilder("'", value.Length + 2);
        foreach (var c in value)
        {
            quoted.Append(c);
            if (c is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B')
            {
                quoted.Append(c);
            }
        }

        return quoted.Append('\'').ToString();
    }
}
