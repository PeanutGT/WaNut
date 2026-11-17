using System.Globalization;
using WaNut.Core.Instances;
using WaNut.Core.Network;
using WaNut.Core.Properties;
using WaNut.Core.Settings;

namespace WaNut.Core.Bridge;

/// <summary>Picks an RCON port no other server on the same host uses for its game or RCON.</summary>
public static class BridgePorts
{
    /// <param name="sameHost">Which other servers run on the same host (this PC, or one VPS).</param>
    /// <param name="isFree">Whether nothing else listens on a port there.</param>
    public static int Pick(InstanceStore store, string serverId, Func<InstanceMetadata, bool> sameHost, Func<int, bool> isFree)
    {
        var taken = new HashSet<int>();
        foreach (var instance in store.LoadAll().Where(i => i.Id != serverId && sameHost(i)))
        {
            var path = Path.Combine(store.InstanceDir(instance.Id), GameSettingsStore.PropertiesFileName);
            if (!File.Exists(path))
            {
                continue;
            }

            var other = PropertiesFile.Load(path);
            foreach (var key in new[] { "rcon.port", "server-port" })
            {
                if (int.TryParse(other.Get(key), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                {
                    taken.Add(port);
                }
            }
        }

        return ServerPorts.Pick(RconSetup.DefaultPort, taken, isFree) ?? RconSetup.DefaultPort;
    }
}
