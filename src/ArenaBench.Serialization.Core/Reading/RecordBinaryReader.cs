namespace ArenaBench.Serialization.Core.Reading;

/// <summary>
/// <see cref="BinaryReader"/> helpers for the T2 baseline. The bulk payload
/// region of the larger records is not consumed by any primitive read, so it
/// has to be drained explicitly - otherwise the next record would start in
/// the middle of the previous one. <see cref="BinaryReader.ReadBytes(int)"/>
/// would allocate per record, hence the caller-owned scratch span.
/// </summary>
public static class RecordBinaryReader
{
    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes or throws.</summary>
    public static void ReadExactly(BinaryReader reader, Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = reader.Read(destination[total..]);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    $"Unexpected end of stream: expected {destination.Length} bytes, got {total}.");
            }

            total += read;
        }
    }
}