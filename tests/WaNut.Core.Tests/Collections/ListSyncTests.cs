using System.Collections.ObjectModel;
using System.Collections.Specialized;
using WaNut.Core.Collections;

namespace WaNut.Core.Tests.Collections;

public sealed class ListSyncTests
{
    private sealed class Row(string name, int version)
    {
        public string Name { get; } = name;

        public int Version { get; set; } = version;
    }

    private static List<NotifyCollectionChangedAction> Watch<T>(ObservableCollection<T> list)
    {
        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => actions.Add(e.Action);
        return actions;
    }

    [Fact]
    public void Sync_SameValues_ChangesNothing()
    {
        var list = new ObservableCollection<string>(["a", "b", "c"]);
        var actions = Watch(list);

        ListSync.Sync(list, ["a", "b", "c"]);

        Assert.Empty(actions);
    }

    [Fact]
    public void Sync_AddsOnlyTheNewValues_AtTheirPlace()
    {
        var list = new ObservableCollection<string>(["a", "c"]);
        var actions = Watch(list);

        ListSync.Sync(list, ["a", "b", "c", "d"]);

        Assert.Equal(["a", "b", "c", "d"], list);
        Assert.Equal([NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add], actions);
    }

    [Fact]
    public void Sync_RemovesOnlyTheGoneValues()
    {
        var list = new ObservableCollection<string>(["a", "b", "c"]);
        var actions = Watch(list);

        ListSync.Sync(list, ["a", "c"]);

        Assert.Equal(["a", "c"], list);
        Assert.Equal([NotifyCollectionChangedAction.Remove], actions);
    }

    [Fact]
    public void Sync_Reordered_MovesWithoutRemovingOrAdding()
    {
        var list = new ObservableCollection<string>(["a", "b", "c"]);
        var actions = Watch(list);

        ListSync.Sync(list, ["c", "a", "b"]);

        Assert.Equal(["c", "a", "b"], list);
        Assert.All(actions, a => Assert.Equal(NotifyCollectionChangedAction.Move, a));
    }

    [Fact]
    public void Sync_WithComparer_KeepsTheExistingSpelling_AndDropsDuplicates()
    {
        var list = new ObservableCollection<string>(["Alex", "alex", "Sam"]);

        ListSync.Sync(list, ["ALEX", "Sam", "sam"], StringComparer.OrdinalIgnoreCase);

        Assert.Equal(["Alex", "Sam"], list);
    }

    [Fact]
    public void Sync_KeptItems_AreTheSameObjects_AndRefreshed()
    {
        var alex = new Row("Alex", 1);
        var list = new ObservableCollection<Row>([alex]);

        ListSync.Sync(list, [("Sam", 1), ("Alex", 2)], s => s.Item1, r => r.Name, s => new Row(s.Item1, s.Item2),
            refresh: (row, s) => row.Version = s.Item2);

        Assert.Same(alex, list[1]);
        Assert.Equal(2, alex.Version);
        Assert.Equal("Sam", list[0].Name);
    }

    [Fact]
    public void Sync_ItemNoLongerCurrent_IsReplacedInPlace()
    {
        var old = new Row("Alex", 1);
        var list = new ObservableCollection<Row>([old, new Row("Sam", 1)]);
        var actions = Watch(list);

        ListSync.Sync(list, [("Alex", 2), ("Sam", 1)], s => s.Item1, r => r.Name, s => new Row(s.Item1, s.Item2),
            isCurrent: (row, s) => row.Version == s.Item2);

        Assert.NotSame(old, list[0]);
        Assert.Equal(2, list[0].Version);
        Assert.Equal([NotifyCollectionChangedAction.Replace], actions);
    }

    [Fact]
    public void Sync_FromEmpty_And_ToEmpty()
    {
        var list = new ObservableCollection<string>();

        ListSync.Sync(list, ["a", "b"]);
        Assert.Equal(["a", "b"], list);

        ListSync.Sync(list, []);
        Assert.Empty(list);
    }
}
