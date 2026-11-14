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
        "for f in chassis_asset_tag sys_vendor product_name; do echo \"dmi_$f=$(cat /sys/class/dmi/id/$f 2>/dev/null)\"; done; " +
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
                "dmi_chassis_asset_tag" => info with { AssetTag = value },
                "dmi_sys_vendor" => info with { SystemVendor = value },
                "dmi_product_name" => info with { ProductName = value },
                _ => info,
            };
        }

        return info;
    }

    /// <summary>
    /// Oracle's asset tag was read on a real instance (2026-11-14). The others follow the providers' docs and are not
    /// checked on real hosts yet; a wrong guess only changes a hint text.
    /// </summary>
    public static VpsProvider ProviderOf(string assetTag, string systemVendor, string productName) =>
        assetTag.Equals("OracleCloud.com", StringComparison.OrdinalIgnoreCase) ? VpsProvider.Oracle
        : systemVendor.StartsWith("Amazon", StringComparison.OrdinalIgnoreCase) || productName.Contains("EC2", StringComparison.Ordinal) ? VpsProvider.Aws
        : assetTag == "7783-7084-3265-9085-8269-3286-77" ? VpsProvider.Azure
        : systemVendor.Equals("Google", StringComparison.OrdinalIgnoreCase) || productName.Contains("Google Compute Engine", StringComparison.Ordinal) ? VpsProvider.Gcp
        : VpsProvider.Other;

    public static HostArchitecture ArchitectureOf(string machine) => machine.ToLowerInvariant() switch
    {
        "x86_64" or "amd64" => HostArchitecture.X64,
        "aarch64" or "arm64" => HostArchitecture.Arm64,
        _ => HostArchitecture.Other,
    };

    private static long KilobytesToBytes(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kb) ? kb * 1024 : 0;
}
