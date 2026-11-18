using WaNut.Core.Storage;

namespace WaNut.Core.Tests.Storage;

public sealed class McFileClassifierTests
{
    [Theory]
    [InlineData("server.jar", false, McFileCategory.ServerCore)]
    [InlineData("libraries", true, McFileCategory.ServerCore)]
    [InlineData("mods/sodium.jar", false, McFileCategory.ModOrPlugin)]
    [InlineData("plugins/Essentials.jar", false, McFileCategory.ModOrPlugin)]
    [InlineData("mods/sodium.jar.disabled", false, McFileCategory.DisabledMod)]
    [InlineData("mods/old.jar.old", false, McFileCategory.DisabledMod)]
    [InlineData("server.properties", false, McFileCategory.Configuration)]
    [InlineData("config/x.toml", false, McFileCategory.Configuration)]
    [InlineData("world", true, McFileCategory.WorldData)]
    [InlineData("world/region/r.0.0.mca", false, McFileCategory.WorldData)]
    [InlineData("world_nether/level.dat", false, McFileCategory.WorldData)]
    [InlineData("logs/latest.log", false, McFileCategory.LogOrCrash)]
    [InlineData("crash-reports/crash-1.txt", false, McFileCategory.LogOrCrash)]
    [InlineData("backups/a.zip", false, McFileCategory.BackupArchive)]
    [InlineData("world_backup.tar.gz", false, McFileCategory.BackupArchive)]
    [InlineData("notes.bin", false, McFileCategory.Other)]
    [InlineData("", true, McFileCategory.Other)]
    public void Classify_Path_ReturnsCategory(string path, bool isDirectory, McFileCategory expected)
    {
        Assert.Equal(expected, McFileClassifier.Classify(path, isDirectory));
    }
}
