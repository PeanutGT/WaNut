using System.Globalization;
using WaNut.Core.Remote.Run;

namespace WaNut.Core.Remote.Firewall;

/// <summary>
/// The VPS's own firewall: reads it with one command and builds the command that lets a server port in. Only ever
/// adds a rule; WaNut never removes rules, so the user's own ones stay as they are.
/// </summary>
public sealed record HostFirewall
{
    /// <summary>One "key=value" per line. Every sudo is -n, so it never waits for a password.</summary>
    public const string Script =
        "if sudo -n true 2>/dev/null; then echo sudo=yes; fi\n" +
        "[ -f /etc/iptables/rules.v4 ] && echo saves=yes\n" +
        "command -v netfilter-persistent >/dev/null 2>&1 && echo persistent=yes\n" +
        "if command -v ufw >/dev/null 2>&1; then sudo -n ufw status 2>/dev/null | sed 's/^/ufw=/'; fi\n" +
        "if command -v firewall-cmd >/dev/null 2>&1 && sudo -n firewall-cmd --state >/dev/null 2>&1; then echo firewalld=running; sudo -n firewall-cmd --list-ports 2>/dev/null | sed 's/^/fwports=/'; fi\n" +
        "sudo -n iptables -S INPUT 2>/dev/null | sed 's/^/ipt=/'\n" +
        "true";

    public static string Command => "sh -c " + RemoteShell.Quote(Script);

    public VpsProvider Provider { get; init; }

    public bool HasSudo { get; init; }

    public HostFirewallKind Kind { get; init; }

    /// <summary>`iptables -S INPUT` as printed: "-P INPUT ACCEPT", then one "-A INPUT ..." per rule.</summary>
    public IReadOnlyList<string> IptablesRules { get; init; } = [];

    /// <summary>/etc/iptables/rules.v4 exists, so a new rule must be saved there to survive a reboot.</summary>
    public bool SavesIptables { get; init; }

    public bool HasNetfilterPersistent { get; init; }

    public IReadOnlyList<string> UfwStatus { get; init; } = [];

    public IReadOnlyList<string> FirewalldPorts { get; init; } = [];

    public static HostFirewall Parse(string output, VpsProvider provider)
    {
        var rules = new List<string>();
        var ufw = new List<string>();
        var ports = new List<string>();
        bool sudo = false, saves = false, persistent = false, firewalld = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var value = line[(equals + 1)..].Trim();
            switch (line[..equals])
            {
                case "sudo": sudo = true; break;
                case "saves": saves = true; break;
                case "persistent": persistent = true; break;
                case "firewalld": firewalld = true; break;
                case "ufw": ufw.Add(value); break;
                case "fwports": ports.AddRange(value.Split(' ', StringSplitOptions.RemoveEmptyEntries)); break;
                case "ipt" when value.Length > 0: rules.Add(value); break;
            }
        }

        var ufwActive = ufw.Any(l => l.StartsWith("Status: active", StringComparison.OrdinalIgnoreCase));
        var kind = !sudo ? HostFirewallKind.Unknown
            : firewalld ? HostFirewallKind.Firewalld

            // Oracle's docs say not to use UFW on its images: it breaks the iptables rules the instance needs.
            : ufwActive && provider != VpsProvider.Oracle ? HostFirewallKind.Ufw
            : BlocksByDefault(rules) ? HostFirewallKind.Iptables
            : HostFirewallKind.None;

        return new HostFirewall
        {
            Provider = provider,
            HasSudo = sudo,
            Kind = kind,
            IptablesRules = rules,
            SavesIptables = saves,
            HasNetfilterPersistent = persistent,
            UfwStatus = ufw,
            FirewalldPorts = ports,
        };
    }

    /// <summary>True when TCP <paramref name="port"/> gets in already; null when the rules couldn't be read.</summary>
    public bool? IsOpen(int port) => Kind switch
    {
        HostFirewallKind.Unknown => null,
        HostFirewallKind.None => true,
        HostFirewallKind.Ufw => UfwStatus.Any(l => UfwAllows(l, port)),
        HostFirewallKind.Firewalld => FirewalldPorts.Any(p => p.EndsWith("/tcp", StringComparison.Ordinal) && PortListHas(p[..^4].Replace('-', ':'), port)),
        _ => IptablesAccepts(port),
    };

    /// <summary>
    /// The command that lets TCP <paramref name="port"/> in and keeps it after a reboot, run as is over SSH; null when
    /// nothing is needed or WaNut can't do it (no password-less sudo).
    /// </summary>
    public string? OpenCommand(int port) => IsOpen(port) == false ? CommandFor(Kind, port, SavesIptables, HasNetfilterPersistent) : null;

    /// <summary>
    /// What the user can paste on the VPS themselves when WaNut may not or can't: for Oracle's iptables layout even
    /// without reading the rules. Null when WaNut doesn't know the firewall.
    /// </summary>
    public string? ManualCommands(int port) => Kind switch
    {
        HostFirewallKind.Unknown when Provider == VpsProvider.Oracle => CommandFor(HostFirewallKind.Iptables, port, saves: true, persistent: true),
        HostFirewallKind.Iptables or HostFirewallKind.Ufw or HostFirewallKind.Firewalld when IsOpen(port) == false => CommandFor(Kind, port, SavesIptables, HasNetfilterPersistent),
        _ => null,
    };

    private static string? CommandFor(HostFirewallKind kind, int port, bool saves, bool persistent)
    {
        var p = port.ToString(CultureInfo.InvariantCulture);
        return kind switch
        {
            // Inserted first, so it comes before the catch-all REJECT; -C first keeps it from being added twice.
            HostFirewallKind.Iptables =>
                $"(sudo iptables -C INPUT -p tcp -m state --state NEW -m tcp --dport {p} -j ACCEPT 2>/dev/null || sudo iptables -I INPUT 1 -p tcp -m state --state NEW -m tcp --dport {p} -j ACCEPT)"
                + (persistent ? " && sudo netfilter-persistent save" : saves ? " && sudo sh -c 'iptables-save > /etc/iptables/rules.v4'" : string.Empty),
            HostFirewallKind.Ufw => $"sudo ufw allow {p}/tcp",
            HostFirewallKind.Firewalld => $"sudo firewall-cmd --permanent --add-port={p}/tcp && sudo firewall-cmd --reload",
            _ => null,
        };
    }

    private static bool BlocksByDefault(IReadOnlyList<string> rules) =>
        rules.Any(r => r is "-P INPUT DROP" or "-P INPUT REJECT") || rules.Any(IsCatchAll);

    /// <summary>A REJECT or DROP that matches everything, like Oracle's "-A INPUT -j REJECT --reject-with icmp-host-prohibited".</summary>
    private static bool IsCatchAll(string rule)
    {
        var words = rule.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 4 && words[0] == "-A" && words[2] == "-j" && words[3] is "REJECT" or "DROP";
    }

    private bool IptablesAccepts(int port)
    {
        foreach (var rule in IptablesRules.Where(r => r.StartsWith("-A ", StringComparison.Ordinal)))
        {
            if (IsCatchAll(rule))
            {
                return false;
            }

            var words = rule.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!words.Contains("ACCEPT") || words.Contains("-i") || words.Contains("-s") || words.Contains("!"))
            {
                continue;
            }

            if (words.SkipWhile(w => w != "-p").Skip(1).FirstOrDefault() is { } protocol && protocol != "tcp")
            {
                continue;
            }

            var ports = words.SkipWhile(w => w is not ("--dport" or "--dports")).Skip(1).FirstOrDefault();

            // An ACCEPT with no port and no protocol lets everything in; one for state RELATED,ESTABLISHED doesn't.
            if (ports is null ? !words.Contains("-p") && !words.Contains("--state") : PortListHas(ports, port))
            {
                return true;
            }
        }

        return !IptablesRules.Any(r => r is "-P INPUT DROP" or "-P INPUT REJECT");
    }

    /// <summary>"25565", "25565:25570" or "22,25565:25570".</summary>
    private static bool PortListHas(string list, int port) =>
        list.Split(',').Any(part =>
        {
            var range = part.Split(':');
            return int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
                && (range.Length == 1 ? from == port
                    : int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out var to) && port >= from && port <= to);
        });

    /// <summary>A `ufw status` line such as "25565/tcp ALLOW Anywhere" or "25565 ALLOW Anywhere".</summary>
    private static bool UfwAllows(string line, int port)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !words[1].StartsWith("ALLOW", StringComparison.Ordinal))
        {
            return false;
        }

        var target = words[0];
        if (target.EndsWith("/udp", StringComparison.Ordinal))
        {
            return false;
        }

        return PortListHas(target.Replace("/tcp", string.Empty, StringComparison.Ordinal), port);
    }
}
