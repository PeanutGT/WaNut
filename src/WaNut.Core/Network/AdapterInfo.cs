using System.Net;
using System.Net.NetworkInformation;

namespace WaNut.Core.Network;

/// <summary>The facts about one network adapter that decide whether WaNut shows its addresses.</summary>
public sealed record AdapterInfo(
    string Name, string Description, NetworkInterfaceType Type, OperationalStatus Status, IReadOnlyList<IPAddress> Addresses);
