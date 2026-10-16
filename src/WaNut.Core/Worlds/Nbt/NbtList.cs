namespace WaNut.Core.Worlds.Nbt;

/// <summary>An NBT list: every item has the same <see cref="ItemType"/>.</summary>
public sealed class NbtList(NbtType itemType, List<NbtTag> items)
{
    public NbtType ItemType { get; } = itemType;

    public List<NbtTag> Items { get; } = items;
}
