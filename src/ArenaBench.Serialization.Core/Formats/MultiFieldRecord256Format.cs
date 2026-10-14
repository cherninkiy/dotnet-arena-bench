using System.Runtime.CompilerServices;

using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// T2 format for <see cref="MultiFieldRecord256"/>: a 56 B header of 14
/// fields plus a 200 B bulk region - four cache lines per record, the widest
/// point of both the field and the size axis.
/// </summary>
public sealed class MultiFieldRecord256Format : IRecordFormat<MultiFieldRecord256>
{
    /// <summary>Declared wire size of one record - 256 B.</summary>
    public const int Size = 256;

    /// <summary>Offset of the bulk region inside a record - 56 B.</summary>
    public const int PayloadOffset = 56;

    /// <summary>Length of the bulk region - 200 B.</summary>
    public const int PayloadSize = 200;

    /// <summary>Base timestamp of the deterministic image.</summary>
    private const long TimestampBase = 1_700_000_000_000_000_000L;

    /// <inheritdoc />
    public static int SizeInBytes => Unsafe.SizeOf<MultiFieldRecord256>();

    /// <inheritdoc />
    public static MultiFieldRecord256 Create(int recordIndex) => new()
    {
        Timestamp = TimestampBase + recordIndex,
        Sequence = recordIndex,
        Demand = 1000.0 + recordIndex,
        Cost = 0.5 + (recordIndex * 0.25),
        FlowId = 10_000 + recordIndex,
        SourceNode = recordIndex,
        TargetNode = recordIndex + 1,
        RouteId = 500 + recordIndex,
        PathLength = (ushort)(4 + (recordIndex % 16)),
        MtuSize = 1500,
        Codec = (byte)(8 + (recordIndex % 4)),
        Flags = (byte)(recordIndex % 2),
        Priority = (byte)(recordIndex % 8),
        Reserved = 0,
    };

    /// <inheritdoc />
    public static void WriteRecord(in MultiFieldRecord256 record, Span<byte> destination, ByteOrder order)
    {
        FieldCodec.WriteInt64(destination, record.Timestamp, order);
        FieldCodec.WriteInt64(destination[8..], record.Sequence, order);
        FieldCodec.WriteDouble(destination[16..], record.Demand, order);
        FieldCodec.WriteDouble(destination[24..], record.Cost, order);
        FieldCodec.WriteInt32(destination[32..], record.FlowId, order);
        FieldCodec.WriteInt32(destination[36..], record.SourceNode, order);
        FieldCodec.WriteInt32(destination[40..], record.TargetNode, order);
        FieldCodec.WriteInt32(destination[44..], record.RouteId, order);
        FieldCodec.WriteUInt16(destination[48..], record.PathLength, order);
        FieldCodec.WriteUInt16(destination[50..], record.MtuSize, order);
        destination[52] = record.Codec;
        destination[53] = record.Flags;
        destination[54] = record.Priority;
        destination[55] = record.Reserved;
        FieldCodec.WritePayloadRamp(destination[PayloadOffset..], record.Sequence);
    }

    /// <inheritdoc />
    public static long ReadBinaryReader(BinaryReader reader, Span<byte> scratch)
    {
        long hash = reader.ReadInt64();
        hash = RecordFingerprint.Mix(hash, reader.ReadInt64());
        hash = RecordFingerprint.Mix(hash, reader.ReadDouble());
        hash = RecordFingerprint.Mix(hash, reader.ReadDouble());
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadUInt16());
        hash = RecordFingerprint.Mix(hash, reader.ReadUInt16());
        hash = RecordFingerprint.Mix(hash, reader.ReadByte());
        hash = RecordFingerprint.Mix(hash, reader.ReadByte());
        hash = RecordFingerprint.Mix(hash, reader.ReadByte());
        hash = RecordFingerprint.Mix(hash, reader.ReadByte());

        // Drain the bulk region so the next record starts where it should;
        // the bytes themselves are not part of the fingerprint.
        RecordBinaryReader.ReadExactly(reader, scratch[..PayloadSize]);
        return hash;
    }

    /// <inheritdoc />
    public static long ReadPrimitives(ReadOnlySpan<byte> source, ByteOrder order)
    {
        long hash = FieldCodec.ReadInt64(source, order);
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt64(source[8..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadDouble(source[16..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadDouble(source[24..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[32..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[36..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[40..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[44..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[48..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[50..], order));
        hash = RecordFingerprint.Mix(hash, source[52]);
        hash = RecordFingerprint.Mix(hash, source[53]);
        hash = RecordFingerprint.Mix(hash, source[54]);
        hash = RecordFingerprint.Mix(hash, source[55]);
        return hash;
    }

    /// <inheritdoc />
    public static long Checksum(in MultiFieldRecord256 record)
    {
        long hash = record.Timestamp;
        hash = RecordFingerprint.Mix(hash, record.Sequence);
        hash = RecordFingerprint.Mix(hash, record.Demand);
        hash = RecordFingerprint.Mix(hash, record.Cost);
        hash = RecordFingerprint.Mix(hash, record.FlowId);
        hash = RecordFingerprint.Mix(hash, record.SourceNode);
        hash = RecordFingerprint.Mix(hash, record.TargetNode);
        hash = RecordFingerprint.Mix(hash, record.RouteId);
        hash = RecordFingerprint.Mix(hash, record.PathLength);
        hash = RecordFingerprint.Mix(hash, record.MtuSize);
        hash = RecordFingerprint.Mix(hash, record.Codec);
        hash = RecordFingerprint.Mix(hash, record.Flags);
        hash = RecordFingerprint.Mix(hash, record.Priority);
        hash = RecordFingerprint.Mix(hash, record.Reserved);
        return hash;
    }
}