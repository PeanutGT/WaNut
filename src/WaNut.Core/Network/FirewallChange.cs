namespace WaNut.Core.Network;

public enum FirewallChange
{
    Done,

    /// <summary>The user answered No on the Windows administrator prompt.</summary>
    Declined,

    Failed,
}
