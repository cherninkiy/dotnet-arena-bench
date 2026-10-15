using System.Runtime.CompilerServices;

using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// T2 format for <see cref="ExtendedRecord128"/>: a 36 B header plus a 92 B
/// bulk region - two cache lines per record.
/// </summary>
public sealed class ExtendedRecord128Format : IRecordFormat<ExtendedRecord128>
{
    /// <summary>Declared wire size of one record - 128 B.</summary>
    public const int Size = 128;

    /// <summary>Offset of the bulk region inside a record - 36 B.</summary>
    public const int PayloadOffset = 36;

    /// <summary>Length of the bulk region - 92 B.</summary>
    public const int PayloadSize = 92;

    /// <summary>Base timestamp of the deterministic image.</summary>
    private const long TimestampBase = 1_700_000_000_000_000_000L;

    /// <inheritdoc />
    public static int SizeInBytes => Unsafe.SizeOf<ExtendedRecord128>();

    /// <inheritdoc />
    public static ExtendedRecord128 Create(int recordIndex) => new()
    {
        Timestamp = TimestampBase + recordIndex,
        Sequence = recordIndex,
        FlowId = 10_000 + recordIndex,
        SourceNode = recordIndex,
        TargetNode = recordIndex + 1,
        Width = 1920,
        Height = 1080,
        Codec = (byte)(8 + (recordIndex % 4)),
        Flags = (byte)(recordIndex % 2),
        Priority = (byte)(recordIndex % 8),
        Reserved = 0,
    };

    /// <inheritdoc />
    public static void WriteRecord(in ExtendedRecord128 record, Span<byte> destination, ByteOrder order)
    {
        FieldCodec.WriteInt64(destination, record.Timestamp, order);
        FieldCodec.WriteInt64(destination[8..], record.Sequence, order);
        FieldCodec.WriteInt32(destination[16..], record.FlowId, order);
        FieldCodec.WriteInt32(destination[20..], record.SourceNode, order);
        FieldCodec.WriteInt32(destination[24..], record.TargetNode, order);
        FieldCodec.WriteUInt16(destination[28..], record.Width, order);
        FieldCodec.WriteUInt16(destination[30..], record.Height, order);
        destination[32] = record.Codec;
        destination[33] = record.Flags;
        destination[34] = record.Priority;
        destination[35] = record.Reserved;
        FieldCodec.WritePayloadRamp(destination[PayloadOffset..], record.Sequence);
    }

    /// <inheritdoc />
    public static long ReadBinaryReader(BinaryReader reader, Span<byte> scratch)
    {
        long hash = reader.ReadInt64();
        hash = RecordFingerprint.Mix(hash, reader.ReadInt64());
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
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[16..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[20..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[24..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[28..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[30..], order));
        hash = RecordFingerprint.Mix(hash, source[32]);
        hash = RecordFingerprint.Mix(hash, source[33]);
        hash = RecordFingerprint.Mix(hash, source[34]);
        hash = RecordFingerprint.Mix(hash, source[35]);
        return hash;
    }

    /// <inheritdoc />
    public static long Checksum(in ExtendedRecord128 record)
    {
        long hash = record.Timestamp;
        hash = RecordFingerprint.Mix(hash, record.Sequence);
        hash = RecordFingerprint.Mix(hash, record.FlowId);
        hash = RecordFingerprint.Mix(hash, record.SourceNode);
        hash = RecordFingerprint.Mix(hash, record.TargetNode);
        hash = RecordFingerprint.Mix(hash, record.Width);
        hash = RecordFingerprint.Mix(hash, record.Height);
        hash = RecordFingerprint.Mix(hash, record.Codec);
        hash = RecordFingerprint.Mix(hash, record.Flags);
        hash = RecordFingerprint.Mix(hash, record.Priority);
        hash = RecordFingerprint.Mix(hash, record.Reserved);
        return hash;
    }
}