using WaNut.Core.Instances;

namespace WaNut.Core.Tests;

public sealed class InstancesFolderMoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WaNutTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Move_GivenServers_MovesEveryServerWithItsFiles()
    {
        var from = Path.Combine(_root, "old");
        var to = Path.Combine(_root, "new");
        Directory.CreateDirectory(Path.Combine(from, "survival", "world"));
        File.WriteAllText(Path.Combine(from, "survival", "world", "level.dat"), "data");
        Directory.CreateDirectory(Path.Combine(from, "creative"));

        InstancesFolderMover.Move(from, to);

        Assert.Equal("data", File.ReadAllText(Path.Combine(to, "survival", "world", "level.dat")));
        Assert.True(Directory.Exists(Path.Combine(to, "creative")));
        Assert.False(Directory.Exists(from));
    }

    [Fact]
    public void Move_GivenMissingOldFolder_CreatesTheNewOne()
    {
        var to = Path.Combine(_root, "new");

        InstancesFolderMover.Move(Path.Combine(_root, "old"), to);

        Assert.True(Directory.Exists(to));
    }

    [Fact]
    public void Check_GivenSameFolder_ReportsSameFolder()
    {
        var dir = Path.Combine(_root, "servers");

        Assert.Equal(MoveProblem.SameFolder, InstancesFolderMover.Check(dir, dir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Check_GivenFolderInsideTheOther_ReportsNested()
    {
        var dir = Path.Combine(_root, "servers");

        Assert.Equal(MoveProblem.NestedFolder, InstancesFolderMover.Check(dir, Path.Combine(dir, "inner")));
        Assert.Equal(MoveProblem.NestedFolder, InstancesFolderMover.Check(Path.Combine(dir, "inner"), dir));
    }

    [Fact]
    public void Check_GivenTargetWithFiles_ReportsNotEmpty()
    {
        var from = Path.Combine(_root, "old");
        var to = Path.Combine(_root, "new");
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(to, "notes.txt"), "mine");

        Assert.Equal(MoveProblem.TargetNotEmpty, InstancesFolderMover.Check(from, to));
        Assert.Throws<InvalidOperationException>(() => InstancesFolderMover.Move(from, to));
    }

    [Fact]
    public void Check_GivenSiblingWithSimilarName_AllowsMove()
    {
        Assert.Null(InstancesFolderMover.Check(Path.Combine(_root, "servers"), Path.Combine(_root, "servers2")));
    }
}
