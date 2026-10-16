using WaNut.Core.Worlds.Nbt;

namespace WaNut.Core.Tests.TestSupport;

/// <summary>Builds small single-player worlds on disk: a level.dat and a few files.</summary>
internal static class WorldFixture
{
    public static NbtCompound Player(string item)
    {
        var player = new NbtCompound();
        player.Set("SelectedItem", NbtTag.Of(item));
        player.Set("XpLevel", NbtTag.Of(30));
        return player;
    }

    /// <summary>A world folder with level.dat, a region file and session.lock.</summary>
    public static string Create(
        string dir, string name = "My World", string? version = "1.21.1", NbtCompound? player = null, string[]? brands = null, long lastPlayed = 1_700_000_000_000)
    {
        Directory.CreateDirectory(Path.Combine(dir, "region"));
        File.WriteAllText(Path.Combine(dir, "region", "r.0.0.mca"), "overworld");
        File.WriteAllText(Path.Combine(dir, "session.lock"), "lock");

        var data = new NbtCompound();
        data.Set("LevelName", NbtTag.Of(name));
        data.Set("LastPlayed", new NbtTag(NbtType.Long, lastPlayed));
        if (version is not null)
        {
            var v = new NbtCompound();
            v.Set("Name", NbtTag.Of(version));
            data.Set("Version", NbtTag.Of(v));
        }

        if (brands is not null)
        {
            data.Set("ServerBrands", new NbtTag(NbtType.List, new NbtList(NbtType.String, brands.Select(NbtTag.Of).ToList())));
        }

        if (player is not null)
        {
            data.Set("Player", NbtTag.Of(player));
        }

        var root = new NbtCompound();
        root.Set("Data", NbtTag.Of(data));
        NbtFile.Write(Path.Combine(dir, "level.dat"), string.Empty, root);
        return dir;
    }
}
