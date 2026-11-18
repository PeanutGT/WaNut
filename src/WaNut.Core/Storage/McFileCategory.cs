namespace WaNut.Core.Storage;

/// <summary>What a file or folder in a server folder is for. Used to filter lists and to decide what clean-up may touch.</summary>
public enum McFileCategory
{
    /// <summary>server.jar, paper-*.jar, fabric-server-launch.jar, libraries/.</summary>
    ServerCore,

    /// <summary>mods/*.jar, plugins/*.jar.</summary>
    ModOrPlugin,

    /// <summary>*.jar.disabled, *.jar.old, and the contents of mods-disabled/.</summary>
    DisabledMod,

    /// <summary>server.properties, *.yml, *.toml, *.json and other settings files.</summary>
    Configuration,

    /// <summary>World folders and region files. Sensitive: clean-up leaves these alone by default.</summary>
    WorldData,

    /// <summary>logs/ and crash-reports/.</summary>
    LogOrCrash,

    /// <summary>backups/ and archives.</summary>
    BackupArchive,

    Other,
}
