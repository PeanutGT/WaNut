using System.Net;
using System.Net.Sockets;

namespace WaNut.Core.Network;

/// <summary>
/// Tells from the address the router reports for itself whether a forwarded port can be reached from the internet
/// at all, without asking any outside website.
/// </summary>
public static class PublicAddressCheck
{
    /// <summary>Null when the address looks public; otherwise why friends still can't reach the port.</summary>
    public static PortForwardOutcome? FindProblem(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        var b = address.GetAddressBytes();
        if (b[0] == 100 && (b[1] & 0xC0) == 64)
        {
            return PortForwardOutcome.Cgnat;
        }

        var isPrivate = b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
        return isPrivate ? PortForwardOutcome.DoubleNat : null;
    }
}
