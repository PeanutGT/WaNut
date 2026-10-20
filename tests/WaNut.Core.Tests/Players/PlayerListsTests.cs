using WaNut.Core.Players;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Players;

public sealed class PlayerListsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("Notch", true)]
    [InlineData("a_b_9", true)]
    [InlineData("x", true)]
    [InlineData("SixteenCharsLong", true)]
    [InlineData("SeventeenCharsLng", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    [InlineData("op Notch; stop", false)]
    public void IsValidName(string name, bool expected)
    {
        Assert.Equal(expected, PlayerLists.IsValidName(name));
    }

    [Fact]
    public void ReadNames_ReadsWhatTheServerWrote()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "ops.json"), """
            [{"uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","name":"Notch","level":4,"bypassesPlayerLimit":false},
             {"uuid":"x","name":"Steve","level":4,"bypassesPlayerLimit":false}]
            """);

        Assert.Equal(["Notch", "Steve"], PlayerLists.ReadNames(_dir.Path, PlayerList.Operators));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{}")]
    public void ReadNames_MissingOrDamaged_IsEmpty(string? contents)
    {
        if (contents is not null)
        {
            File.WriteAllText(Path.Combine(_dir.Path, "whitelist.json"), contents);
        }

        Assert.Empty(PlayerLists.ReadNames(_dir.Path, PlayerList.Whitelist));
    }

    [Fact]
    public void Queue_AddThenRemove_LeavesNothing()
    {
        var pending = PlayerLists.Queue([], PlayerList.Whitelist, "Alex", add: true, fileNames: []);
        Assert.Equal(["whitelist add Alex"], pending);

        pending = PlayerLists.Queue(pending, PlayerList.Whitelist, "Alex", add: false, fileNames: []);
        Assert.Empty(pending);
    }

    [Fact]
    public void Queue_RemoveFromFile_QueuesRemove()
    {
        var pending = PlayerLists.Queue(["op Steve"], PlayerList.Operators, "Notch", add: false, fileNames: ["Notch"]);

        Assert.Equal(["op Steve", "deop Notch"], pending);
    }

    [Fact]
    public void WithPending_AppliesQueuedChanges()
    {
        var shown = PlayerLists.WithPending(["Notch", "Steve"], ["whitelist add Alex", "whitelist remove notch", "op Herobrine"], PlayerList.Whitelist);

        Assert.Equal(["Steve", "Alex"], shown);
    }

    [Fact]
    public void ReadNames_BanList_ReadsBannedPlayers()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "banned-players.json"), """
            [{"uuid":"x","name":"Griefer","created":"2026-10-20 10:00:00 +0800","source":"Server","expires":"forever","reason":"Banned by an operator."}]
            """);

        Assert.Equal(["Griefer"], PlayerLists.ReadNames(_dir.Path, PlayerList.Banned));
    }

    [Theory]
    [InlineData(null, "ban Griefer")]
    [InlineData("   ", "ban Griefer")]
    [InlineData("Broke  the house", "ban Griefer Broke the house")]
    [InlineData("line one\nstop", "ban Griefer line one stop")]
    public void BanCommand_CleansTheReason(string? reason, string expected)
    {
        Assert.Equal(expected, PlayerLists.BanCommand("Griefer", reason));
    }

    [Fact]
    public void CleanReason_LongReason_IsCut()
    {
        Assert.Equal(PlayerLists.MaxReasonLength, PlayerLists.CleanReason(new string('a', 300)).Length);
    }

    [Fact]
    public void KickCommand_WithoutReason_IsJustTheName()
    {
        Assert.Equal("kick Alex", PlayerLists.KickCommand("Alex", null));
    }

    [Fact]
    public void Queue_BanWithReasonThenPardon_LeavesNothing()
    {
        var pending = PlayerLists.Queue([], PlayerList.Banned, "Griefer", add: true, fileNames: [], reason: "griefing");
        Assert.Equal(["ban Griefer griefing"], pending);

        pending = PlayerLists.Queue(pending, PlayerList.Banned, "griefer", add: false, fileNames: []);
        Assert.Empty(pending);
    }

    [Fact]
    public void WithPending_BanWithReason_AddsTheName()
    {
        var shown = PlayerLists.WithPending([], ["ban Griefer took my diamonds", "whitelist add Alex"], PlayerList.Banned);

        Assert.Equal(["Griefer"], shown);
    }

    [Fact]
    public void Queue_OtherListsAndCommands_AreKept()
    {
        var pending = PlayerLists.Queue(["gamerule keepInventory true", "op Alex"], PlayerList.Banned, "Alex", add: true, fileNames: []);

        Assert.Equal(["gamerule keepInventory true", "op Alex", "ban Alex"], pending);
    }
}
