namespace WaNut.Core.Remote.Run;

/// <summary>
/// The small sh script that runs one server on the VPS (plan decision 2A: no compiled agent). It starts java in the
/// background with its input on a FIFO and its output in a log, writes the pids, waits, and writes the exit code.
/// Everything lives in the server folder's .wanut-run, which never syncs. The server keeps running when WaNut closes.
/// </summary>
public static class RunnerScript
{
    public const string ScriptPath = "wanut/bin/wanut-run.sh";

    /// <summary>The run folder inside a server folder; <see cref="Sync.SyncRules"/> never syncs ".wanut-*".</summary>
    public const string RunFolder = ".wanut-run";

    public const string Script = """
        #!/bin/sh
        # WaNut runs one Minecraft server: wanut-run.sh <server folder> <java> <arguments...>
        dir=$1
        shift
        run="$dir/.wanut-run"
        mkdir -p "$run" || exit 1
        echo $$ > "$run/pid"
        rm -f "$run/in"
        : > "$run/console.log"
        mkfifo "$run/in" || { echo 1 > "$run/exit"; exit 1; }
        cd "$dir" || { echo 1 > "$run/exit"; exit 1; }
        # Read-write, so java never sees the end of its input between commands and a writer never waits.
        "$@" <>"$run/in" >>"$run/console.log" 2>&1 &
        echo $! > "$run/java.pid"
        wait $!
        code=$?
        echo $code > "$run/exit.wanut-part" && mv "$run/exit.wanut-part" "$run/exit"
        """;

    public const string StopScriptPath = "wanut/bin/wanut-stop.sh";

    /// <summary>
    /// systemd's ExecStop: asks a running server to save and stop, the same as WaNut does, and waits for it. Also run
    /// when the server already ended on its own, so it does nothing then.
    /// </summary>
    public const string StopScript = """
        #!/bin/sh
        # WaNut stops one Minecraft server: wanut-stop.sh <server folder>
        run="$1/.wanut-run"
        p=$(cat "$run/java.pid" 2>/dev/null) || exit 0
        kill -0 "$p" 2>/dev/null || exit 0
        printf 'stop\n' 1<>"$run/in"
        i=0
        while kill -0 "$p" 2>/dev/null && [ $i -lt 85 ]; do sleep 1; i=$((i+1)); done
        """;

    /// <summary>Writes both scripts (again each start, so a newer WaNut brings its newer scripts).</summary>
    public static string InstallCommand() =>
        $"mkdir -p {RemoteShell.Home("wanut/bin")} && cat > {RemoteShell.Home(ScriptPath)} <<'WANUT_SCRIPT_END'\n{Script.ReplaceLineEndings("\n")}\nWANUT_SCRIPT_END\n"
        + $"cat > {RemoteShell.Home(StopScriptPath)} <<'WANUT_SCRIPT_END'\n{StopScript.ReplaceLineEndings("\n")}\nWANUT_SCRIPT_END\n";

    public static string RunPath(string serverRoot, string file) => $"{serverRoot}/{RunFolder}/{file}";

    /// <summary>
    /// The run's start.sh: runs the runner with this start's java and arguments. Written through stdin (see
    /// <see cref="WriteStartScriptCommand"/>), so no argument ever goes through a shell command line on its own.
    /// </summary>
    public static string StartScript(string serverRoot, string javaPath, IReadOnlyList<string> arguments) =>
        $"#!/bin/sh\nexec sh {RemoteShell.Home(ScriptPath)} {RemoteShell.Home(serverRoot)} {RemoteShell.Home(javaPath)} {string.Join(' ', arguments.Select(RemoteShell.Quote))}\n";

    /// <summary>Saves stdin as the run's start.sh and clears what the last run left.</summary>
    public static string WriteStartScriptCommand(string serverRoot)
    {
        var run = RemoteShell.Home($"{serverRoot}/{RunFolder}");
        return $"run={run} && mkdir -p \"$run\" && rm -f \"$run/pid\" \"$run/java.pid\" \"$run/exit\" && cat > \"$run/start.sh\"";
    }

    /// <summary>Prints "systemd" when the account may run sudo without a password on a systemd host, else "plain".</summary>
    public const string ModeCommand =
        "if [ -d /run/systemd/system ] && command -v systemctl >/dev/null 2>&1 && sudo -n true 2>/dev/null; then echo systemd; else echo plain; fi; printf '%s\n%s\n' \"$HOME\" \"$(id -un)\"";

    /// <summary>Starts start.sh detached from the SSH session (no sudo, or no systemd).</summary>
    public static string LaunchPlainCommand(string serverRoot) =>
        $"setsid nohup sh {RemoteShell.Home(RunPath(serverRoot, "start.sh"))} </dev/null >/dev/null 2>&1 &\n" + WaitForPid(serverRoot);

    /// <summary>The systemd service's name for a server: one per SSH user and server folder.</summary>
    public static string UnitName(string user, string serverRoot)
    {
        var name = $"{user}-{serverRoot.Split('/')[^1]}";
        var safe = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '-').ToArray());
        return $"wanut-{safe}.service";
    }

    /// <summary>
    /// The service for <see cref="StartUnitCommand"/>: runs start.sh as the SSH user and asks the server to save on
    /// a systemctl stop or a VPS shutdown. Not enabled at boot: WaNut brings the world back after every run.
    /// </summary>
    public static string Unit(string home, string user, string serverRoot) =>
        string.Join('\n',
            "[Unit]",
            $"Description=WaNut Minecraft server ({SystemdText(serverRoot)})",
            "After=network-online.target",
            "Wants=network-online.target",
            string.Empty,
            "[Service]",
            "Type=simple",
            $"User={user}",
            $"Environment={SystemdQuote("HOME=" + home)}",
            $"ExecStart=/bin/sh {SystemdQuote($"{home}/{RunPath(serverRoot, "start.sh")}")}",
            $"ExecStop=/bin/sh {SystemdQuote($"{home}/{StopScriptPath}")} {SystemdQuote($"{home}/{serverRoot}")}",
            "TimeoutStopSec=90",
            "KillMode=mixed",
            string.Empty);

    /// <summary>Saves stdin as the unit and starts it.</summary>
    public static string StartUnitCommand(string unitName, string serverRoot) =>
        $"sudo -n tee /etc/systemd/system/{unitName} >/dev/null && sudo -n systemctl daemon-reload && (sudo -n systemctl reset-failed {unitName} 2>/dev/null; true) && sudo -n systemctl start {unitName}\n"
        + WaitForPid(serverRoot);

    /// <summary>A user name systemd's User= takes as it is.</summary>
    public static bool IsPlainUserName(string user) =>
        user.Length is > 0 and <= 32 && (char.IsAsciiLetterLower(user[0]) || user[0] == '_')
        && user.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-' or '.');

    /// <summary>One systemd word: quoted, with its own escapes for \\, ", % and $.</summary>
    public static string SystemdQuote(string value) => "\"" + SystemdText(value).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string SystemdText(string value) =>
        value.Replace("%", "%%", StringComparison.Ordinal).Replace("$", "$$", StringComparison.Ordinal).ReplaceLineEndings(" ");

    /// <summary>Waits until java started (or the runner gave up) and prints the runner's pid.</summary>
    private static string WaitForPid(string serverRoot) => string.Join('\n',
        $"run={RemoteShell.Home($"{serverRoot}/{RunFolder}")}",
        "i=0",
        "while [ ! -s \"$run/java.pid\" ] && [ ! -s \"$run/exit\" ] && [ $i -lt 100 ]; do sleep 0.1; i=$((i+1)); done",
        "cat \"$run/pid\"");

    /// <summary>Streams the console from byte <paramref name="offset"/> until the runner <paramref name="runnerPid"/> ends.</summary>
    public static string TailCommand(string serverRoot, int runnerPid, long offset) =>
        $"exec tail --pid={runnerPid} -c +{offset + 1} -F {RemoteShell.Home(RunPath(serverRoot, "console.log"))} 2>/dev/null";

    /// <summary>Prints the exit code, "running" while the runner is alive, or "gone" when it ended without one.</summary>
    public static string StateCommand(string serverRoot, int runnerPid)
    {
        var exit = RemoteShell.Home(RunPath(serverRoot, "exit"));
        return $"if [ -s {exit} ]; then cat {exit}; elif kill -0 {runnerPid} 2>/dev/null; then echo running; else echo gone; fi";
    }

    /// <summary>Copies stdin (the console lines) into the FIFO. Opened read-write, so it never waits when java is gone.</summary>
    public static string SendCommand(string serverRoot) =>
        $"exec cat 1<>{RemoteShell.Home(RunPath(serverRoot, "in"))}";

    public static string KillCommand(string serverRoot) =>
        $"kill -9 \"$(cat {RemoteShell.Home(RunPath(serverRoot, "java.pid"))})\" 2>/dev/null; true";

    /// <summary>Four lines: CPU ticks used, resident memory in kB, ticks per second, cores.</summary>
    public static string UsageCommand(string serverRoot) =>
        $"p=$(cat {RemoteShell.Home(RunPath(serverRoot, "java.pid"))}) && awk '{{print $14+$15}}' /proc/$p/stat && awk '/^VmRSS:/{{print $2}}' /proc/$p/status && getconf CLK_TCK && nproc";
}
