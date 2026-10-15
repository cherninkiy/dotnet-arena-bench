namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// Builds the byte image the T2 benches read: a fixed number of fixed-layout
/// records encoded in one byte order (concept.md 7.4 - one payload, many
/// strategies over it).
/// </summary>
public static class T2Payload
{
    /// <summary>Records per payload: 256 x 16..256 B = 4..64 KiB.</summary>
    public const int DefaultRecordCount = 256;

    /// <summary>Encodes <paramref name="recordCount"/> records into a fresh byte image.</summary>
    public static byte[] Create<TFormat, TRecord>(int recordCount, ByteOrder order)
        where TFormat : IRecordFormat<TRecord>
        where TRecord : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);

        byte[] payload = new byte[checked(recordCount * TFormat.SizeInBytes)];
        for (int i = 0; i < recordCount; i++)
        {
            TRecord record = TFormat.Create(i);
            TFormat.WriteRecord(in record, payload.AsSpan(i * TFormat.SizeInBytes, TFormat.SizeInBytes), order);
        }

        return payload;
    }

    /// <summary>
    /// Fingerprint of the same image, computed from the generator instead of
    /// the bytes: exactly what every strategy has to reproduce.
    /// </summary>
    public static long Fingerprint<TFormat, TRecord>(int recordCount)
        where TFormat : IRecordFormat<TRecord>
        where TRecord : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);

        long sum = 0;
        for (int i = 0; i < recordCount; i++)
        {
            TRecord record = TFormat.Create(i);
            sum += TFormat.Checksum(in record);
        }

        return sum;
    }
}