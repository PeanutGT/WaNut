using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace WaNut.Core.Worlds.Nbt;

/// <summary>
/// Reads and writes NBT files such as level.dat and playerdata/&lt;uuid&gt;.dat: a gzipped, big-endian tree whose
/// root is one named compound. Strings use Java's "modified UTF-8".
/// </summary>
public static class NbtFile
{
    // Deeper trees only come from damaged or hostile files; Minecraft itself stops at 512.
    private const int MaxDepth = 512;

    public static (string RootName, NbtCompound Root) Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads gzipped NBT, or plain NBT when the data does not start with the gzip header.</summary>
    public static (string RootName, NbtCompound Root) Read(Stream stream)
    {
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            var plain = new MemoryStream();
            gzip.CopyTo(plain);
            bytes = plain.ToArray();
        }

        try
        {
            var reader = new Reader(bytes);
            if ((NbtType)reader.Byte() != NbtType.Compound)
            {
                throw new InvalidDataException("The NBT root is not a compound.");
            }

            var name = reader.String();
            return (name, reader.Compound(0));
        }
        catch (IndexOutOfRangeException e)
        {
            throw new InvalidDataException("The NBT data ends too early.", e);
        }
    }

    /// <summary>Writes gzipped NBT through a temporary file, so a failed write never leaves half a file.</summary>
    public static void Write(string path, string rootName, NbtCompound root)
    {
        var temp = path + ".wanut-tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            Write(gzip, rootName, root);
        }

        File.Move(temp, path, overwrite: true);
    }

    public static void Write(Stream stream, string rootName, NbtCompound root)
    {
        var writer = new Writer(stream);
        writer.Byte((byte)NbtType.Compound);
        writer.String(rootName);
        writer.Compound(root);
    }

    private sealed class Reader(byte[] data)
    {
        private int _position;

        public byte Byte() => data[_position++];

        public ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || _position + count > data.Length)
            {
                throw new InvalidDataException("The NBT data ends too early.");
            }

            var span = data.AsSpan(_position, count);
            _position += count;
            return span;
        }

        public short Short() => BinaryPrimitives.ReadInt16BigEndian(Take(2));

        public int Int() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

        public long Long() => BinaryPrimitives.ReadInt64BigEndian(Take(8));

        public string String() => ModifiedUtf8.Decode(Take((ushort)Short()));

        public NbtCompound Compound(int depth)
        {
            var compound = new NbtCompound();
            while (true)
            {
                var type = (NbtType)Byte();
                if (type == NbtType.End)
                {
                    return compound;
                }

                var name = String();
                compound.Set(name, Tag(type, depth + 1));
            }
        }

        private NbtTag Tag(NbtType type, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidDataException("The NBT data is nested too deeply.");
            }

            return new NbtTag(type, type switch
            {
                NbtType.Byte => (sbyte)Byte(),
                NbtType.Short => Short(),
                NbtType.Int => Int(),
                NbtType.Long => Long(),
                NbtType.Float => BinaryPrimitives.ReadSingleBigEndian(Take(4)),
                NbtType.Double => BinaryPrimitives.ReadDoubleBigEndian(Take(8)),
                NbtType.ByteArray => Take(Int()).ToArray(),
                NbtType.String => String(),
                NbtType.List => List(depth),
                NbtType.Compound => Compound(depth),
                NbtType.IntArray => Array(Int(), 4, s => BinaryPrimitives.ReadInt32BigEndian(s)),
                NbtType.LongArray => Array(Int(), 8, s => BinaryPrimitives.ReadInt64BigEndian(s)),
                _ => throw new InvalidDataException($"Unknown NBT tag type {(byte)type}."),
            });
        }

        private NbtList List(int depth)
        {
            var itemType = (NbtType)Byte();
            var count = Int();
            if (count < 0 || count > data.Length - _position)
            {
                throw new InvalidDataException("The NBT list length is not possible.");
            }

            var items = new List<NbtTag>(count);
            for (var i = 0; i < count; i++)
            {
                items.Add(Tag(itemType, depth));
            }

            return new NbtList(itemType, items);
        }

        private T[] Array<T>(int count, int size, Func<ReadOnlySpan<byte>, T> read)
        {
            if (count < 0 || (long)count * size > data.Length - _position)
            {
                throw new InvalidDataException("The NBT array length is not possible.");
            }

            var items = new T[count];
            for (var i = 0; i < count; i++)
            {
                items[i] = read(Take(size));
            }

            return items;
        }
    }

    private sealed class Writer(Stream stream)
    {
        private readonly byte[] _buffer = new byte[8];

        public void Byte(byte value) => stream.WriteByte(value);

        public void String(string value)
        {
            var bytes = ModifiedUtf8.Encode(value);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException("An NBT string is longer than 65535 bytes.");
            }

            Short((short)(ushort)bytes.Length);
            stream.Write(bytes);
        }

        public void Compound(NbtCompound compound)
        {
            foreach (var (name, tag) in compound.Entries)
            {
                Byte((byte)tag.Type);
                String(name);
                Payload(tag);
            }

            Byte((byte)NbtType.End);
        }

        private void Short(short value)
        {
            BinaryPrimitives.WriteInt16BigEndian(_buffer, value);
            stream.Write(_buffer, 0, 2);
        }

        private void Int(int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(_buffer, value);
            stream.Write(_buffer, 0, 4);
        }

        private void Long(long value)
        {
            BinaryPrimitives.WriteInt64BigEndian(_buffer, value);
            stream.Write(_buffer, 0, 8);
        }

        private void Payload(NbtTag tag)
        {
            switch (tag.Value)
            {
                case sbyte b: Byte((byte)b); break;
                case short s: Short(s); break;
                case int i: Int(i); break;
                case long l: Long(l); break;
                case float f: BinaryPrimitives.WriteSingleBigEndian(_buffer, f); stream.Write(_buffer, 0, 4); break;
                case double d: BinaryPrimitives.WriteDoubleBigEndian(_buffer, d); stream.Write(_buffer, 0, 8); break;
                case byte[] bytes: Int(bytes.Length); stream.Write(bytes); break;
                case string text: String(text); break;
                case NbtList list:
                    Byte((byte)list.ItemType);
                    Int(list.Items.Count);
                    foreach (var item in list.Items)
                    {
                        Payload(item);
                    }

                    break;
                case NbtCompound compound: Compound(compound); break;
                case int[] ints:
                    Int(ints.Length);
                    foreach (var i in ints)
                    {
                        Int(i);
                    }

                    break;
                case long[] longs:
                    Int(longs.Length);
                    foreach (var l in longs)
                    {
                        Long(l);
                    }

                    break;
                default:
                    throw new InvalidDataException($"An NBT {tag.Type} tag holds a {tag.Value.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Java's modified UTF-8: each UTF-16 unit is encoded on its own (so characters outside the BMP become two
    /// 3-byte surrogates) and U+0000 is written as two bytes. Working unit by unit makes it map exactly onto .NET strings.
    /// </summary>
    private static class ModifiedUtf8
    {
        public static byte[] Encode(string text)
        {
            var bytes = new List<byte>(text.Length);
            foreach (var c in text)
            {
                if (c is >= '\u0001' and <= '\u007f')
                {
                    bytes.Add((byte)c);
                }
                else if (c <= '߿')
                {
                    bytes.Add((byte)(0xc0 | (c >> 6)));
                    bytes.Add((byte)(0x80 | (c & 0x3f)));
                }
                else
                {
                    bytes.Add((byte)(0xe0 | (c >> 12)));
                    bytes.Add((byte)(0x80 | ((c >> 6) & 0x3f)));
                    bytes.Add((byte)(0x80 | (c & 0x3f)));
                }
            }

            return bytes.ToArray();
        }

        public static string Decode(ReadOnlySpan<byte> bytes)
        {
            var builder = new StringBuilder(bytes.Length);
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                if (b < 0x80)
                {
                    builder.Append((char)b);
                }
                else if ((b & 0xe0) == 0xc0 && i + 1 < bytes.Length)
                {
                    builder.Append((char)(((b & 0x1f) << 6) | (bytes[++i] & 0x3f)));
                }
                else if ((b & 0xf0) == 0xe0 && i + 2 < bytes.Length)
                {
                    builder.Append((char)(((b & 0x0f) << 12) | ((bytes[i + 1] & 0x3f) << 6) | (bytes[i + 2] & 0x3f)));
                    i += 2;
                }
                else
                {
                    throw new InvalidDataException("An NBT string is not valid modified UTF-8.");
                }
            }

            return builder.ToString();
        }
    }
}
