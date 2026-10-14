using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ArenaBench.Serialization.Core.Formats;

namespace ArenaBench.Serialization.Core.Reading;

/// <summary>
/// The T2 read strategies of the matrix (concept.md 5.1). Every method walks
/// the same record image and returns the summed per-record fingerprint, so
/// the strategies are interchangeable, comparable and cross-checkable
/// (ArenaBench.Serialization.Tests does exactly that).
/// </summary>
public static class T2Strategies
{
    /// <summary>S01-S04: field by field with BinaryReader over a stream.</summary>
    public static long BinaryReader<TFormat, TRecord>(BinaryReader reader, int recordCount, Span<byte> scratch)
        where TFormat : IRecordFormat<TRecord>
        where TRecord : unmanaged
    {
        long sum = 0;
        for (int i = 0; i < recordCount; i++)
        {
            sum += TFormat.ReadBinaryReader(reader, scratch);
        }

        return sum;
    }

    /// <summary>S05-S06: field by field with BinaryPrimitives over a span, explicit byte order.</summary>
    public static long Primitives<TFormat, TRecord>(ReadOnlySpan<byte> payload, int recordCount, ByteOrder order)
        where TFormat : IRecordFormat<TRecord>
        where TRecord : unmanaged
    {
        long sum = 0;
        for (int i = 0; i < recordCount; i++)
        {
            sum += TFormat.ReadPrimitives(payload.Slice(i * TFormat.SizeInBytes, TFormat.SizeInBytes), order);
        }

        return sum;
    }

    /// <summary>S07: one typed struct copy per record via MemoryMarshal.Read.</summary>
    public static long MemoryMarshalRead<TFormat, TRecord>(ReadOnlySpan<byte> payload, int recordCount)
        where TFormat : IRecordFormat<TRecord>
        where TRecord : unmanaged
    {
        long sum = 0;
        for (int i = 0; i < recordCount; i++)
        {
            TRecord record = MemoryMarshal.Read<TRecord>(
                payload.Slice(i * TFormat.SizeInBytes, TFormat.SizeInBytes));
            sum += TFormat.Checksum(in record);
        }

        return sum;
    }

    /// <summary>S08: one typed struct read per record via Unsafe.ReadUnaligned, no intermediate copy.</summary>
    public static long UnsafeReadUnaligned<TFormat, TRecord>(ReadOnlySpan<byte> payload, int recordCount)
        where TFormat : IRecordFormat<TRecord>
        where TRecord : unmanaged
    {
        long sum = 0;
        for (int i = 0; i < recordCount; i++)
        {
            ref byte origin = ref MemoryMarshal.GetReference(
                payload.Slice(i * TFormat.SizeInBytes, TFormat.SizeInBytes));
            TRecord record = Unsafe.ReadUnaligned<TRecord>(ref origin);
            sum += TFormat.Checksum(in record);
        }

        return sum;
    }

    /// <summary>S09: bulk struct view via MemoryMarshal.Cast, no per-record copy at all.</summary>
    public static long Cast<TFormat, TRecord>(ReadOnlySpan<byte> payload, int recordCount)
        where TFormat : IRecordFormat<TRecord>
        where TRecord : unmanaged
    {
        ReadOnlySpan<TRecord> records = MemoryMarshal.Cast<byte, TRecord>(payload);
        long sum = 0;
        for (int i = 0; i < recordCount; i++)
        {
            sum += TFormat.Checksum(in records[i]);
        }

        return sum;
    }
}