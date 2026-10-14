using System.Runtime.CompilerServices;

using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// T2 format for <see cref="VideoRecord64"/>: a 28 B header plus a 36 B bulk
/// region - one cache line per record. The bulk bytes are fingerprinted by no
/// strategy: the stream baseline skips them, the struct views read them as an
/// opaque part of the view.
/// </summary>
public sealed class VideoRecord64Format : IRecordFormat<VideoRecord64>
{
    /// <summary>Declared wire size of one record - 64 B.</summary>
    public const int Size = 64;

    /// <summary>Offset of the bulk region inside a record - 28 B.</summary>
    public const int PayloadOffset = 28;

    /// <summary>Length of the bulk region - 36 B.</summary>
    public const int PayloadSize = 36;

    /// <summary>Base timestamp of the deterministic image.</summary>
    private const long TimestampBase = 1_700_000_000_000_000_000L;

    /// <inheritdoc />
    public static int SizeInBytes => Unsafe.SizeOf<VideoRecord64>();

    /// <inheritdoc />
    public static VideoRecord64 Create(int recordIndex) => new()
    {
        Timestamp = TimestampBase + recordIndex,
        Sequence = recordIndex,
        FrameId = 100_000 + recordIndex,
        Width = 1920,
        Height = 1080,
        Codec = (byte)(8 + (recordIndex % 4)),
        Flags = (byte)(recordIndex % 2),
        Reserved = 0,
    };

    /// <inheritdoc />
    public static void WriteRecord(in VideoRecord64 record, Span<byte> destination, ByteOrder order)
    {
        FieldCodec.WriteInt64(destination, record.Timestamp, order);
        FieldCodec.WriteInt64(destination[8..], record.Sequence, order);
        FieldCodec.WriteInt32(destination[16..], record.FrameId, order);
        FieldCodec.WriteUInt16(destination[20..], record.Width, order);
        FieldCodec.WriteUInt16(destination[22..], record.Height, order);
        destination[24] = record.Codec;
        destination[25] = record.Flags;
        FieldCodec.WriteUInt16(destination[26..], record.Reserved, order);
        FieldCodec.WritePayloadRamp(destination[PayloadOffset..], record.Sequence);
    }

    /// <inheritdoc />
    public static long ReadBinaryReader(BinaryReader reader, Span<byte> scratch)
    {
        long hash = reader.ReadInt64();
        hash = RecordFingerprint.Mix(hash, reader.ReadInt64());
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadUInt16());
        hash = RecordFingerprint.Mix(hash, reader.ReadUInt16());
        hash = RecordFingerprint.Mix(hash, reader.ReadByte());
        hash = RecordFingerprint.Mix(hash, reader.ReadByte());
        hash = RecordFingerprint.Mix(hash, reader.ReadUInt16());

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
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[20..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[22..], order));
        hash = RecordFingerprint.Mix(hash, source[24]);
        hash = RecordFingerprint.Mix(hash, source[25]);
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[26..], order));
        return hash;
    }

    /// <inheritdoc />
    public static long Checksum(in VideoRecord64 record)
    {
        long hash = record.Timestamp;
        hash = RecordFingerprint.Mix(hash, record.Sequence);
        hash = RecordFingerprint.Mix(hash, record.FrameId);
        hash = RecordFingerprint.Mix(hash, record.Width);
        hash = RecordFingerprint.Mix(hash, record.Height);
        hash = RecordFingerprint.Mix(hash, record.Codec);
        hash = RecordFingerprint.Mix(hash, record.Flags);
        hash = RecordFingerprint.Mix(hash, record.Reserved);
        return hash;
    }
}