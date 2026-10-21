using System.Net;
using System.Net.Sockets;

namespace WaNut.Core.Crashes;

/// <summary>Helpers behind the fix buttons of a crash diagnosis.</summary>
public static class CrashFixes
{
    // Mod ids whose Modrinth project has another slug. Fabric API's modules each have their own id ("fabric-networking-api-v1").
    private static readonly Dictionary<string, string> Slugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fabric"] = "fabric-api",
        ["architectury"] = "architectury-api",
        ["cloth_config"] = "cloth-config",
        ["cloth-config2"] = "cloth-config",
        ["fabric_language_kotlin"] = "fabric-language-kotlin",
        ["kotlinforforge"] = "kotlin-for-forge",
        ["geckolib3"] = "geckolib",
    };

    /// <summary>The Modrinth project (slug) that most likely provides a mod id; mod ids and slugs usually match.</summary>
    public static string ModrinthProjectFor(string modId)
    {
        if (Slugs.TryGetValue(modId, out var slug))
        {
            return slug;
        }

        if (modId.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase) && !modId.StartsWith("fabric-language-", StringComparison.OrdinalIgnoreCase))
        {
            return "fabric-api";
        }

        return modId.Replace('_', '-').ToLowerInvariant();
    }

    /// <summary>
    /// The first port after <paramref name="current"/> that <paramref name="isFree"/> accepts, or null if none of the
    /// next hundred is.
    /// </summary>
    public static int? NextFreePort(int current, Func<int, bool> isFree)
    {
        for (var port = current + 1; port <= Math.Min(current + 100, 65535); port++)
        {
            if (isFree(port))
            {
                return port;
            }
        }

        return null;
    }

    /// <summary>True when nothing on this PC listens on the TCP port.</summary>
    public static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
