namespace WaNut.Core.Tunnels;

/// <summary>How far the user got on playit.gg's claim page.</summary>
public enum PlayitClaimStatus
{
    /// <summary>The page was not opened yet.</summary>
    WaitingForVisit,

    /// <summary>The page is open; the user has not pressed approve yet.</summary>
    WaitingForApproval,

    Accepted,
    Rejected,
}
