using WaNut.Core.Hotplug.Readiness;

namespace WaNut.Core.Tests.Hotplug;

/// <summary>An inbox whose files change over scripted moments of a fake clock.</summary>
internal sealed class FakeInbox(Tests.TestSupport.FakeClock clock) : IReadinessSource
{
    private readonly Dictionary<string, (long Size, DateTime Time, string Text)> _files = [];
    private readonly List<(TimeSpan At, Action Act)> _script = [];
    private readonly DateTimeOffset _start = clock.Now;

    public HashSet<string> Locked { get; } = [];

    public int Snapshots { get; private set; }

    public FakeInbox At(double seconds, Action<FakeInbox> act)
    {
        _script.Add((TimeSpan.FromSeconds(seconds), () => act(this)));
        return this;
    }

    public FakeInbox Put(string name, long size, string text = "")
    {
        _files[name] = (size, clock.Now.UtcDateTime, text);
        return this;
    }

    public FakeInbox Remove(string name)
    {
        _files.Remove(name);
        return this;
    }

    public Task<IReadOnlyList<ReadinessEntry>> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        Snapshots++;
        foreach (var due in _script.Where(s => _start + s.At <= clock.Now).ToList())
        {
            _script.Remove(due);
            due.Act();
        }

        return Task.FromResult<IReadOnlyList<ReadinessEntry>>(_files.Select(f => new ReadinessEntry(f.Key, f.Value.Size, f.Value.Time)).ToList());
    }

    public Task<bool> CanReadExclusivelyAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(!Locked.Contains(name));

    public Task<string> ReadTextAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(_files[name].Text);
}
