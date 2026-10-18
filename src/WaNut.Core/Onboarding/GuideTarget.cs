namespace WaNut.Core.Onboarding;

/// <summary>The part of the main window a guide stop points at.</summary>
public enum GuideTarget
{
    /// <summary>No highlight: the peanut talks in the middle of the window.</summary>
    None,
    CreateServer,
    ServerList,
    AppSettings,
    StartStop,

    /// <summary>A tab of the selected server, picked by <see cref="GuideStop.TabIndex"/>.</summary>
    Tab,
}
