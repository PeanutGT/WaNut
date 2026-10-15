using System.Net;

namespace WaNut.Core.Network;

/// <param name="AdapterName">The adapter's name in Windows, such as "Wi-Fi" or "Radmin VPN".</param>
public sealed record LocalAddress(string AdapterName, AdapterKind Kind, IPAddress Address);
