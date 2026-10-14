using System.Runtime.CompilerServices;

using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// T2 format for <see cref="Commodity32"/>: 32 B of scalars, no bulk region.
/// The record carries the two 64-bit floats of the ladder, which makes the
/// double path of the fingerprint visible in the matrix.
/// </summary>
public sealed class Commodity32Format : IRecordFormat<Commodity32>
{
    /// <summary>Declared wire size of one record - 32 B.</summary>
    public const int Size = 32;

    /// <summary>Base timestamp of the deterministic image.</summary>
    private const long TimestampBase = 1_700_000_000_000_000_000L;

    /// <inheritdoc />
    public static int SizeInBytes => Unsafe.SizeOf<Commodity32>();

    /// <inheritdoc />
    public static Commodity32 Create(int recordIndex) => new()
    {
        Timestamp = TimestampBase + recordIndex,
        Demand = 1000.0 + recordIndex,
        CommodityId = 100 + recordIndex,
        SourceNode = recordIndex,
        TargetNode = recordIndex + 1,
        PathLength = (ushort)(4 + (recordIndex % 16)),
        Reserved = 0,
    };

    /// <inheritdoc />
    public static void WriteRecord(in Commodity32 record, Span<byte> destination, ByteOrder order)
    {
        FieldCodec.WriteInt64(destination, record.Timestamp, order);
        FieldCodec.WriteDouble(destination[8..], record.Demand, order);
        FieldCodec.WriteInt32(destination[16..], record.CommodityId, order);
        FieldCodec.WriteInt32(destination[20..], record.SourceNode, order);
        FieldCodec.WriteInt32(destination[24..], record.TargetNode, order);
        FieldCodec.WriteUInt16(destination[28..], record.PathLength, order);
        FieldCodec.WriteUInt16(destination[30..], record.Reserved, order);
    }

    /// <inheritdoc />
    public static long ReadBinaryReader(BinaryReader reader, Span<byte> scratch)
    {
        long hash = reader.ReadInt64();
        hash = RecordFingerprint.Mix(hash, reader.ReadDouble());
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadUInt16());
        hash = RecordFingerprint.Mix(hash, reader.ReadUInt16());
        return hash;
    }

    /// <inheritdoc />
    public static long ReadPrimitives(ReadOnlySpan<byte> source, ByteOrder order)
    {
        long hash = FieldCodec.ReadInt64(source, order);
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadDouble(source[8..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[16..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[20..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[24..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[28..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[30..], order));
        return hash;
    }

    /// <inheritdoc />
    public static long Checksum(in Commodity32 record)
    {
        long hash = record.Timestamp;
        hash = RecordFingerprint.Mix(hash, record.Demand);
        hash = RecordFingerprint.Mix(hash, record.CommodityId);
        hash = RecordFingerprint.Mix(hash, record.SourceNode);
        hash = RecordFingerprint.Mix(hash, record.TargetNode);
        hash = RecordFingerprint.Mix(hash, record.PathLength);
        hash = RecordFingerprint.Mix(hash, record.Reserved);
        return hash;
    }
}