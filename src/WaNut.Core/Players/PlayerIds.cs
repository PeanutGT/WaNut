using System.Security.Cryptography;
using System.Text;

namespace WaNut.Core.Players;

public static class PlayerIds
{
    /// <summary>
    /// The UUID a server with online-mode=false gives a player: Java's UUID.nameUUIDFromBytes of
    /// "OfflinePlayer:&lt;name&gt;", a version 3 (MD5) UUID.
    /// </summary>
    public static Guid OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        // Parsed from text so the Guid prints exactly the bytes in Java's order.
        return Guid.ParseExact(Convert.ToHexString(hash), "N");
    }
}
