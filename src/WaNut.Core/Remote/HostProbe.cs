using System.Globalization;

namespace WaNut.Core.Remote;

/// <summary>Asks a VPS what it is: one POSIX sh command, one "key=value" per line, so any login shell can run it.</summary>
public static class HostProbe
{
    /// <summary>
    /// Run through sh so a fish or zsh login shell doesn't matter. Contains no single quote, so it can sit inside
    /// sh -c '...'. os-release is read in a subshell so its variables don't leak.
    /// </summary>
    public const string Script =
        "echo \"os=$(uname -s)\"; echo \"machine=$(uname -m)\"; " +
        "if [ -r /etc/os-release ]; then (. /etc/os-release; echo \"distro=$ID\"; echo \"like=$ID_LIKE\"; echo \"name=$PRETTY_NAME\"); fi; " +
        "awk \"/^MemTotal:/ {print \\\"memkb=\\\" \\$2}\" /proc/meminfo 2>/dev/null; " +
        "df -Pk \"$HOME\" 2>/dev/null | awk \"NR==2 {print \\\"diskkb=\\\" \\$4}\"; " +
        "for t in curl wget tar; do command -v $t >/dev/null 2>&1 && echo \"has=$t\"; done; true";

    public static string Command => $"sh -c '{Script}'";

    public static HostInfo Parse(string output)
    {
        var info = new HostInfo();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals];
            var value = line[(equals + 1)..].Trim();
            info = key switch
            {
                "os" => info with { Os = value },
                "machine" => info with { Machine = value, Architecture = ArchitectureOf(value) },
                "distro" => info with { DistroId = value.Trim('"').ToLowerInvariant() },
                "like" => info with { DistroLike = value.Trim('"').ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries) },
                "name" => info with { DistroName = value.Trim('"') },
                "memkb" => info with { MemoryBytes = KilobytesToBytes(value) },
                "diskkb" => info with { FreeDiskBytes = KilobytesToBytes(value) },
                "has" when value == "curl" => info with { HasCurl = true },
                "has" when value == "wget" => info with { HasWget = true },
                "has" when value == "tar" => info with { HasTar = true },
                _ => info,
            };
        }

        return info;
    }

    public static HostArchitecture ArchitectureOf(string machine) => machine.ToLowerInvariant() switch
    {
        "x86_64" or "amd64" => HostArchitecture.X64,
        "aarch64" or "arm64" => HostArchitecture.Arm64,
        _ => HostArchitecture.Other,
    };

    private static long KilobytesToBytes(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kb) ? kb * 1024 : 0;
}
