using System.Buffers.Binary;

namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// Primitive accessors over the T2 wire image. Every accessor is explicit
/// about byte order: whether a swap happens is the decision of the strategy
/// (S05 vs S06), never of the codec.
/// </summary>
public static class FieldCodec
{
    /// <summary>Reads a 64-bit integer in the requested byte order.</summary>
    public static long ReadInt64(ReadOnlySpan<byte> source, ByteOrder order) => order is ByteOrder.LittleEndian
        ? BinaryPrimitives.ReadInt64LittleEndian(source)
        : BinaryPrimitives.ReadInt64BigEndian(source);

    /// <summary>Reads a 64-bit float in the requested byte order.</summary>
    public static double ReadDouble(ReadOnlySpan<byte> source, ByteOrder order) => order is ByteOrder.LittleEndian
        ? BinaryPrimitives.ReadDoubleLittleEndian(source)
        : BinaryPrimitives.ReadDoubleBigEndian(source);

    /// <summary>Reads a 32-bit integer in the requested byte order.</summary>
    public static int ReadInt32(ReadOnlySpan<byte> source, ByteOrder order) => order is ByteOrder.LittleEndian
        ? BinaryPrimitives.ReadInt32LittleEndian(source)
        : BinaryPrimitives.ReadInt32BigEndian(source);

    /// <summary>Reads a 16-bit integer in the requested byte order.</summary>
    public static ushort ReadUInt16(ReadOnlySpan<byte> source, ByteOrder order) => order is ByteOrder.LittleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(source)
        : BinaryPrimitives.ReadUInt16BigEndian(source);

    /// <summary>Writes a 64-bit integer in the requested byte order.</summary>
    public static void WriteInt64(Span<byte> destination, long value, ByteOrder order)
    {
        if (order is ByteOrder.LittleEndian)
        {
            BinaryPrimitives.WriteInt64LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt64BigEndian(destination, value);
        }
    }

    /// <summary>Writes a 64-bit float in the requested byte order.</summary>
    public static void WriteDouble(Span<byte> destination, double value, ByteOrder order)
    {
        if (order is ByteOrder.LittleEndian)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleBigEndian(destination, value);
        }
    }

    /// <summary>Writes a 32-bit integer in the requested byte order.</summary>
    public static void WriteInt32(Span<byte> destination, int value, ByteOrder order)
    {
        if (order is ByteOrder.LittleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(destination, value);
        }
    }

    /// <summary>Writes a 16-bit integer in the requested byte order.</summary>
    public static void WriteUInt16(Span<byte> destination, ushort value, ByteOrder order)
    {
        if (order is ByteOrder.LittleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        }
    }

    /// <summary>
    /// Deterministic bulk bytes for the fixed payload region: a ramp derived
    /// from the record's sequence number. No strategy parses these bytes, but
    /// the stream baseline still has to read (and skip) them, and keeping them
    /// non-trivial avoids measuring an unrealistically uniform image.
    /// </summary>
    public static void WritePayloadRamp(Span<byte> destination, long seed)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)(seed + i);
        }
    }
}