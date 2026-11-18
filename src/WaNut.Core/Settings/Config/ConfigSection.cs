namespace WaNut.Core.Settings.Config;

/// <summary>Where a server.properties setting is shown on the Server settings page.</summary>
public enum ConfigSection
{
    /// <summary>Always visible.</summary>
    Core,

    /// <summary>Collapsed group: world and gameplay rules.</summary>
    WorldRules,

    /// <summary>Collapsed group: connection and performance.</summary>
    NetworkPerf,

    /// <summary>Only in the full property list, such as rarely used keys and keys a modpack added.</summary>
    RawOnly,
}
