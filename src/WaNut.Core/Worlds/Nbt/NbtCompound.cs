namespace WaNut.Core.Worlds.Nbt;

/// <summary>An NBT compound: named tags, kept in the order they were read.</summary>
public sealed class NbtCompound
{
    private readonly List<KeyValuePair<string, NbtTag>> _entries = [];

    public IReadOnlyList<KeyValuePair<string, NbtTag>> Entries => _entries;

    public NbtTag? this[string name] => _entries.FirstOrDefault(e => e.Key == name).Value;

    public NbtCompound? Compound(string name) => this[name]?.AsCompound;

    public string? String(string name) => this[name]?.AsString;

    public long? Number(string name) => this[name]?.AsLong;

    /// <summary>Replaces the tag with that name in place, or adds it at the end.</summary>
    public void Set(string name, NbtTag tag)
    {
        var index = _entries.FindIndex(e => e.Key == name);
        if (index >= 0)
        {
            _entries[index] = new(name, tag);
        }
        else
        {
            _entries.Add(new(name, tag));
        }
    }

    public bool Remove(string name) => _entries.RemoveAll(e => e.Key == name) > 0;
}
