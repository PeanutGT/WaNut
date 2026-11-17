using System.Globalization;
using WaNut.Core.Remote.Run;

namespace WaNut.Core.Bridge.Vps;

/// <summary>
/// The sh text that puts a bridge on a VPS and looks after it. With systemd and password-less sudo it lives in
/// /opt/wanut-bridge as a service that restarts on a crash and starts at boot (B1); otherwise in ~/wanut/bridge as a
/// setsid background loop that restarts on a crash but not after a reboot (B2). One shared executable, one folder
/// per server with its config (chmod 600). Every value goes through <see cref="RemoteShell.Quote"/>, and every
/// script runs through sh -c, so the login shell doesn't matter.
/// </summary>
public static class VpsBridgeScripts
{
    public const string SystemBase = "/opt/wanut-bridge";

    /// <summary>Under the SSH user's home folder.</summary>
    public const string HomeBase = "wanut/bridge";

    public const string Executable = "WaNut.Bridge";

    /// <summary>One "key=value" per line: mode (systemd or plain), home, user, group.</summary>
    public const string ModeScript =
        "if [ -d /run/systemd/system ] && command -v systemctl >/dev/null 2>&1 && sudo -n true 2>/dev/null; then echo mode=systemd; else echo mode=plain; fi\n" +
        "echo \"home=$HOME\"; echo \"user=$(id -un)\"; echo \"group=$(id -gn)\"";

    public static string Sh(string script) => "sh -c " + RemoteShell.Quote(script);

    public static string BaseFor(bool systemd, string home) => systemd ? SystemBase : $"{home}/{HomeBase}";

    public static string UnitName(string serverId) =>
        "wanut-bridge-" + new string(serverId.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '-').ToArray()) + ".service";

    public static string ServerFolder(string basePath, string serverId) => $"{basePath}/{serverId}";

    public static string ConfigPath(string basePath, string serverId) => $"{ServerFolder(basePath, serverId)}/{BridgeConfigJson.FileName}";

    /// <summary>Makes the folders, owned by the SSH user so SFTP can write there; the server folder is private.</summary>
    public static string PrepareFolders(bool systemd, string basePath, string serverId, string user, string group)
    {
        var make = systemd
            ? $"sudo -n install -d -m 755 -o {RemoteShell.Quote(user)} -g {RemoteShell.Quote(group)} {RemoteShell.Quote(basePath)}"
            : $"mkdir -p {RemoteShell.Quote(basePath)}";
        return $"{make} && mkdir -p {RemoteShell.Quote(ServerFolder(basePath, serverId))} && chmod 700 {RemoteShell.Quote(ServerFolder(basePath, serverId))}";
    }

    /// <summary>Prints the executable's SHA-256, or nothing when it isn't there (or sha256sum is missing).</summary>
    public static string HashCommand(string basePath) =>
        $"sha256sum {RemoteShell.Quote($"{basePath}/{Executable}")} 2>/dev/null | cut -d ' ' -f 1; true";

    /// <summary>Puts an uploaded ".part" in place: executable, and swapped in one rename so a running bridge keeps its copy.</summary>
    public static string InstallExecutable(string basePath)
    {
        var target = RemoteShell.Quote($"{basePath}/{Executable}");
        var part = RemoteShell.Quote($"{basePath}/{Executable}.part");
        return $"chmod 755 {part} && mv -f {part} {target}";
    }

    /// <summary>Saves stdin as the config, never readable by others, keeping the previous one as .bak for a rollback.</summary>
    public static string WriteConfig(string basePath, string serverId)
    {
        var config = RemoteShell.Quote(ConfigPath(basePath, serverId));
        var part = RemoteShell.Quote(ConfigPath(basePath, serverId) + ".part");
        var backup = RemoteShell.Quote(ConfigPath(basePath, serverId) + ".bak");
        return $"umask 077; if [ -f {config} ]; then cp -p {config} {backup}; else rm -f {backup}; fi; cat > {part} && chmod 600 {part} && mv -f {part} {config}";
    }

    /// <summary>Puts the .bak back, or removes the config when there was none.</summary>
    public static string RestoreConfig(string basePath, string serverId)
    {
        var config = RemoteShell.Quote(ConfigPath(basePath, serverId));
        var backup = RemoteShell.Quote(ConfigPath(basePath, serverId) + ".bak");
        return $"if [ -f {backup} ]; then mv -f {backup} {config}; else rm -f {config}; fi";
    }

    public static string DropConfigBackup(string basePath, string serverId) => $"rm -f {RemoteShell.Quote(ConfigPath(basePath, serverId) + ".bak")}";

    /// <summary>
    /// The service: runs as the SSH user (who can read the server's log), restarts 5 s after a crash, but not after exit
    /// code 2 (an unusable config would only fail again), and starts at boot.
    /// </summary>
    public static string Unit(string basePath, string serverId, string user) =>
        string.Join('\n',
            "[Unit]",
            $"Description=WaNut Discord bridge ({serverId.ReplaceLineEndings(" ")})",
            "After=network-online.target",
            "Wants=network-online.target",
            string.Empty,
            "[Service]",
            "Type=simple",
            $"User={user}",
            $"ExecStart={RunnerScript.SystemdQuote($"{basePath}/{Executable}")} --config {RunnerScript.SystemdQuote(ConfigPath(basePath, serverId))}",
            "Restart=always",
            "RestartSec=5",
            "RestartPreventExitStatus=2",
            string.Empty,
            "[Install]",
            "WantedBy=multi-user.target",
            string.Empty);

    /// <summary>Prints "existed" when the unit file was already there, then saves stdin as it, enables and (re)starts it.</summary>
    public static string StartUnit(string serverId)
    {
        var unit = UnitName(serverId);
        return $"if [ -f /etc/systemd/system/{unit} ]; then echo existed; fi; sudo -n tee /etc/systemd/system/{unit} >/dev/null && sudo -n systemctl daemon-reload && sudo -n systemctl enable {unit} >/dev/null 2>&1 && sudo -n systemctl restart {unit}";
    }

    /// <summary>Undoes <see cref="StartUnit"/> for a unit that didn't exist before.</summary>
    public static string DeleteUnit(string serverId)
    {
        var unit = UnitName(serverId);
        return $"sudo -n systemctl disable --now {unit} >/dev/null 2>&1; sudo -n rm -f /etc/systemd/system/{unit}; sudo -n systemctl daemon-reload; true";
    }

    /// <summary>
    /// Starts the background loop and prints its pid: the bridge again 5 s after a crash, and done after a clean stop
    /// (exit 0) or an unusable config (exit 2). setsid makes the loop a process group, which is what a stop ends.
    /// </summary>
    public static string StartPlain(string basePath, string serverId)
    {
        var folder = ServerFolder(basePath, serverId);
        const string Loop = "echo $$ > \"$2/pid\"; while :; do \"$0\" --config \"$1\" --log \"$2/bridge.log\"; c=$?; if [ $c -eq 0 ] || [ $c -eq 2 ]; then exit $c; fi; sleep 5; done";
        return $"cd {RemoteShell.Quote(folder)} || exit 1\nrm -f pid\nsetsid sh -c {RemoteShell.Quote(Loop)} {RemoteShell.Quote($"{basePath}/{Executable}")} {RemoteShell.Quote(ConfigPath(basePath, serverId))} {RemoteShell.Quote(folder)} </dev/null >runner.log 2>&1 &\n"
            + "i=0; while [ ! -s pid ] && [ $i -lt 50 ]; do sleep 0.1; i=$((i+1)); done; cat pid";
    }

    /// <summary>
    /// Stops whatever runs for the server, either way: the service, then the background loop's process group (TERM,
    /// which the bridge takes as a clean stop). Prints "was-running" when something ran. The unit stays installed.
    /// </summary>
    public static string Stop(string home, string serverId)
    {
        var unit = UnitName(serverId);
        var pidFile = RemoteShell.Quote($"{ServerFolder(BaseFor(false, home), serverId)}/pid");
        return string.Join('\n',
            $"if systemctl is-active --quiet {unit} 2>/dev/null; then echo was-running; sudo -n systemctl stop {unit}; fi",
            $"if p=$(cat {pidFile} 2>/dev/null) && [ -n \"$p\" ] && kill -0 \"$p\" 2>/dev/null; then echo was-running; kill -TERM -\"$p\" 2>/dev/null || kill -TERM \"$p\"",
            "  i=0; while kill -0 \"$p\" 2>/dev/null && [ $i -lt 50 ]; do sleep 0.1; i=$((i+1)); done",
            "  kill -KILL -\"$p\" 2>/dev/null; fi",
            $"rm -f {pidFile}; true");
    }

    /// <summary>
    /// What runs: the bridge's own status JSON line when it answers; else "unit &lt;state&gt;" for a service, "plain
    /// running" / "plain stopped" for the loop, or "none" when the server has no bridge on this VPS.
    /// </summary>
    public static string Status(string home, string serverId)
    {
        var id = RemoteShell.Quote(serverId);
        var unit = UnitName(serverId);
        var system = RemoteShell.Quote(SystemBase);
        var homeBase = RemoteShell.Quote(BaseFor(false, home));
        return string.Join('\n',
            "base=",
            $"for b in {system} {homeBase}; do if [ -f \"$b\"/{id}/{BridgeConfigJson.FileName} ]; then base=$b; break; fi; done",
            "if [ -z \"$base\" ]; then echo none; exit 0; fi",
            $"if \"$base\"/{Executable} --status {id} 2>/dev/null; then exit 0; fi",
            $"if [ -f /etc/systemd/system/{unit} ]; then echo \"unit $(systemctl is-active {unit} 2>/dev/null)\"; exit 0; fi",
            $"if p=$(cat \"$base\"/{id}/pid 2>/dev/null) && [ -n \"$p\" ] && kill -0 \"$p\" 2>/dev/null; then echo plain running; else echo plain stopped; fi");
    }

    /// <summary>The bridge's last log lines: the journal for a service, bridge.log for the loop. The bridge never logs secrets.</summary>
    public static string LastLog(bool systemd, string basePath, string serverId, int lines = 15) =>
        systemd
            ? $"sudo -n journalctl -u {UnitName(serverId)} -n {lines.ToString(CultureInfo.InvariantCulture)} --no-pager -o cat 2>/dev/null; true"
            : $"tail -n {lines.ToString(CultureInfo.InvariantCulture)} {RemoteShell.Quote(ServerFolder(basePath, serverId) + "/bridge.log")} {RemoteShell.Quote(ServerFolder(basePath, serverId) + "/runner.log")} 2>/dev/null; true";

    /// <summary>The bridge's recent log wherever it runs: the journal when it is a service, else bridge.log. Prints nothing when there is none.</summary>
    public static string RecentLog(string home, string serverId, int lines)
    {
        var id = RemoteShell.Quote(serverId);
        var unit = UnitName(serverId);
        var count = lines.ToString(CultureInfo.InvariantCulture);
        return string.Join('\n',
            $"if [ -f /etc/systemd/system/{unit} ]; then sudo -n journalctl -u {unit} -n {count} --no-pager -o cat 2>/dev/null; exit 0; fi",
            $"for b in {RemoteShell.Quote(SystemBase)} {RemoteShell.Quote(BaseFor(false, home))}; do",
            $"  if [ -f \"$b\"/{id}/bridge.log ]; then tail -n {count} \"$b\"/{id}/bridge.log; break; fi",
            "done; true");
    }

    /// <summary>Removes the service and the server's folder; the shared executable too once no server uses it.</summary>
    public static string Remove(string home, string serverId)
    {
        var id = RemoteShell.Quote(serverId);
        return string.Join('\n',
            DeleteUnit(serverId),
            $"for b in {RemoteShell.Quote(SystemBase)} {RemoteShell.Quote(BaseFor(false, home))}; do",
            $"  [ -d \"$b\"/{id} ] && rm -rf \"$b\"/{id}",
            $"  if [ -d \"$b\" ] && [ -z \"$(find \"$b\" -mindepth 1 -maxdepth 1 -type d 2>/dev/null)\" ]; then rm -f \"$b\"/{Executable}; fi",
            "done; true");
    }
}
