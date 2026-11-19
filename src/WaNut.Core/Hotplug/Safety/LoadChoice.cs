namespace WaNut.Core.Hotplug.Safety;

/// <summary>What the user picked when the server was busy.</summary>
public enum LoadChoice
{
    /// <summary>Hot-plug now; players may notice a short stutter.</summary>
    RunNow,

    /// <summary>Wait until fewer players are online, then hot-plug by itself.</summary>
    Queue,

    /// <summary>Don't hot-plug.</summary>
    Cancel,
}
