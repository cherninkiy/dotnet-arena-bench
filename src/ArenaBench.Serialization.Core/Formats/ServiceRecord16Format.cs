using System.Runtime.CompilerServices;

using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// T2 format for <see cref="ServiceRecord16"/>: 16 B, no bulk region, so
/// every strategy reads all five fields (concept.md, S01-S09).
/// </summary>
public sealed class ServiceRecord16Format : IRecordFormat<ServiceRecord16>
{
    /// <summary>Declared wire size of one record - 16 B.</summary>
    public const int Size = 16;

    /// <summary>Base timestamp of the deterministic image.</summary>
    private const long TimestampBase = 1_700_000_000_000_000_000L;

    /// <inheritdoc />
    public static int SizeInBytes => Unsafe.SizeOf<ServiceRecord16>();

    /// <inheritdoc />
    public static ServiceRecord16 Create(int recordIndex) => new()
    {
        Timestamp = TimestampBase + recordIndex,
        ServiceId = 1000 + recordIndex,
        Port = (ushort)(5060 + recordIndex),
        Codec = (byte)(8 + (recordIndex % 4)),
        Flags = (byte)(recordIndex % 2),
    };

    /// <inheritdoc />
    public static void WriteRecord(in ServiceRecord16 record, Span<byte> destination, ByteOrder order)
    {
        FieldCodec.WriteInt64(destination, record.Timestamp, order);
        FieldCodec.WriteInt32(destination[8..], record.ServiceId, order);
        FieldCodec.WriteUInt16(destination[12..], record.Port, order);
        destination[14] = record.Codec;
        destination[15] = record.Flags;
    }

    /// <inheritdoc />
    public static long ReadBinaryReader(BinaryReader reader, Span<byte> scratch)
    {
        long hash = reader.ReadInt64();
        hash = RecordFingerprint.Mix(hash, reader.ReadInt32());
        hash = RecordFingerprint.Mix(hash, reader.ReadUInt16());
        hash = RecordFingerprint.Mix(hash, reader.ReadByte());
        hash = RecordFingerprint.Mix(hash, reader.ReadByte());

        // No bulk region: the 16 bytes above are the whole record, so the
        // scratch span stays untouched.
        return hash;
    }

    /// <inheritdoc />
    public static long ReadPrimitives(ReadOnlySpan<byte> source, ByteOrder order)
    {
        long hash = FieldCodec.ReadInt64(source, order);
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadInt32(source[8..], order));
        hash = RecordFingerprint.Mix(hash, FieldCodec.ReadUInt16(source[12..], order));
        hash = RecordFingerprint.Mix(hash, source[14]);
        hash = RecordFingerprint.Mix(hash, source[15]);
        return hash;
    }

    /// <inheritdoc />
    public static long Checksum(in ServiceRecord16 record)
    {
        long hash = record.Timestamp;
        hash = RecordFingerprint.Mix(hash, record.ServiceId);
        hash = RecordFingerprint.Mix(hash, record.Port);
        hash = RecordFingerprint.Mix(hash, record.Codec);
        hash = RecordFingerprint.Mix(hash, record.Flags);
        return hash;
    }
}