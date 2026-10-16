namespace WaNut.Core.Worlds.Nbt;

/// <summary>
/// One NBT value. <see cref="Value"/> holds sbyte, short, int, long, float, double, byte[], string,
/// <see cref="NbtList"/>, <see cref="NbtCompound"/>, int[] or long[], matching <see cref="Type"/>.
/// Everything read is kept, so a file can be written back without losing what WaNut does not understand.
/// </summary>
public sealed class NbtTag(NbtType type, object value)
{
    public NbtType Type { get; } = type;

    public object Value { get; } = value;

    public NbtCompound? AsCompound => Value as NbtCompound;

    public string? AsString => Value as string;

    /// <summary>The number as a long for any whole-number tag (byte, short, int, long); otherwise null.</summary>
    public long? AsLong => Value switch
    {
        sbyte b => b,
        short s => s,
        int i => i,
        long l => l,
        _ => null,
    };

    public static NbtTag Of(NbtCompound compound) => new(NbtType.Compound, compound);

    public static NbtTag Of(string text) => new(NbtType.String, text);

    public static NbtTag Of(int number) => new(NbtType.Int, number);
}
