using WaNut.Core.Hotplug.Readiness;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hotplug;

public sealed class AppliedFilterSourceTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task SnapshotAsync_HidesAppliedUntilChanged()
    {
        var zip = Path.Combine(_dir.Path, "a.zip");
        File.WriteAllText(zip, "one");
        File.WriteAllText(Path.Combine(_dir.Path, "notes.txt"), "x");
        var source = new AppliedFilterSource(new LocalReadinessSource(_dir.Path));

        var first = await source.SnapshotAsync();
        Assert.Equal(["a.zip"], first.Select(e => e.Name));

        source.MarkApplied(first);
        Assert.Empty(await source.SnapshotAsync());

        File.WriteAllText(zip, "two, longer");
        Assert.Equal(["a.zip"], (await source.SnapshotAsync()).Select(e => e.Name));
    }
}
