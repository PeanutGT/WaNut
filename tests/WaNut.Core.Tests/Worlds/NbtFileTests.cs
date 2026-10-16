using System.IO.Compression;
using WaNut.Core.Worlds.Nbt;

namespace WaNut.Core.Tests.Worlds;

public sealed class NbtFileTests
{
    private static NbtCompound Sample()
    {
        var inner = new NbtCompound();
        inner.Set("name", NbtTag.Of("花生 🥜 \0 end"));
        var root = new NbtCompound();
        root.Set("b", new NbtTag(NbtType.Byte, (sbyte)-3));
        root.Set("s", new NbtTag(NbtType.Short, (short)300));
        root.Set("i", NbtTag.Of(-70000));
        root.Set("l", new NbtTag(NbtType.Long, long.MaxValue));
        root.Set("f", new NbtTag(NbtType.Float, 1.5f));
        root.Set("d", new NbtTag(NbtType.Double, -2.25));
        root.Set("ba", new NbtTag(NbtType.ByteArray, new byte[] { 1, 2, 255 }));
        root.Set("ia", new NbtTag(NbtType.IntArray, new[] { 1, -1 }));
        root.Set("la", new NbtTag(NbtType.LongArray, new[] { 5L, -5L }));
        root.Set("list", new NbtTag(NbtType.List, new NbtList(NbtType.Compound, [NbtTag.Of(inner)])));
        root.Set("empty", new NbtTag(NbtType.List, new NbtList(NbtType.End, [])));
        return root;
    }

    [Fact]
    public void Write_ThenRead_KeepsEveryTagAndOrder()
    {
        using var stream = new MemoryStream();
        NbtFile.Write(stream, "root", Sample());
        stream.Position = 0;

        var (name, root) = NbtFile.Read(stream);

        Assert.Equal("root", name);
        Assert.Equal(["b", "s", "i", "l", "f", "d", "ba", "ia", "la", "list", "empty"], root.Entries.Select(e => e.Key));
        Assert.Equal((sbyte)-3, root["b"]!.Value);
        Assert.Equal(long.MaxValue, root.Number("l"));
        Assert.Equal(1.5f, root["f"]!.Value);
        Assert.Equal(new byte[] { 1, 2, 255 }, root["ba"]!.Value);
        Assert.Equal(new[] { 5L, -5L }, root["la"]!.Value);
        var list = (NbtList)root["list"]!.Value;
        Assert.Equal("花生 🥜 \0 end", list.Items[0].AsCompound!.String("name"));
    }

    [Fact]
    public void Write_ThenReadAgain_IsByteForByteTheSame()
    {
        using var first = new MemoryStream();
        NbtFile.Write(first, "", Sample());
        first.Position = 0;
        var (_, root) = NbtFile.Read(first);

        using var second = new MemoryStream();
        NbtFile.Write(second, "", root);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public void Read_GzippedFile_Decompresses()
    {
        using var temp = new TestSupport.TempDir();
        var path = Path.Combine(temp.Path, "level.dat");
        NbtFile.Write(path, "", Sample());

        using (var file = File.OpenRead(path))
        {
            Assert.Equal(0x1f, file.ReadByte());
        }

        Assert.Equal(300, NbtFile.Read(path).Root.Number("s"));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 8, 0, 0 })]
    [InlineData(new byte[] { 10, 0, 0, 3, 0, 1 })]
    [InlineData(new byte[] { 10, 0, 0, 9, 0, 1, (byte)'x', 1, 127, 255, 255, 255 })]
    public void Read_DamagedData_ThrowsInvalidData(byte[] data)
    {
        Assert.Throws<InvalidDataException>(() => NbtFile.Read(new MemoryStream(data)));
    }

    [Fact]
    public void Read_DamagedGzip_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => NbtFile.Read(new MemoryStream([0x1f, 0x8b, 8, 0, 1, 2, 3])));
    }
}
