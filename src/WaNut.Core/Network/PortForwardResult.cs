using System.Net;

namespace WaNut.Core.Network;

/// <summary>The outcome of asking the router for the port, and the router's internet address when it gave one.</summary>
public sealed record PortForwardResult(PortForwardOutcome Outcome, IPAddress? PublicAddress = null);
