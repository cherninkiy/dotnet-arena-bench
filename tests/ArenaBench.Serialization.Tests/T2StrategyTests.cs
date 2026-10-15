using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Sources;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Tests;

/// <summary>
/// Cross-check of the T2 strategy matrix: every reader of concept.md 5.1 has
/// to return the same fingerprint for the same image, for both byte orders,
/// and the stream baseline has to survive the payload being read from a
/// MemoryStream. This is what makes the S01-S09 numbers comparable.
/// </summary>
public sealed class T2StrategyTests
{
    private const int RecordCount = 32;

    [Fact]
    public void ServiceRecord16_StrategiesAgree() =>
        AssertStrategiesAgree<ServiceRecord16Format, ServiceRecord16>();

    [Fact]
    public void Commodity32_StrategiesAgree() =>
        AssertStrategiesAgree<Commodity32Format, Commodity32>();

    [Fact]
    public void VideoRecord64_StrategiesAgree() =>
        AssertStrategiesAgree<VideoRecord64Format, VideoRecord64>();

    [Fact]
    public void ExtendedRecord128_StrategiesAgree() =>
        AssertStrategiesAgree<ExtendedRecord128Format, ExtendedRecord128>();

    [Fact]
    public void MultiFieldRecord256_StrategiesAgree() =>
        AssertStrategiesAgree<MultiFieldRecord256Format, MultiFieldRecord256>();

    [Fact]
    public void Payload_HoldsOneRecordPerCount()
    {
        byte[] payload = T2Payload.Create<MultiFieldRecord256Format, MultiFieldRecord256>(7, ByteOrder.LittleEndian);
        Assert.Equal(7 * MultiFieldRecord256Format.Size, payload.Length);
    }

    [Fact]
    public void Payload_CarriesGeneratedBytes()
    {
        byte[] payload = T2Payload.Create<VideoRecord64Format, VideoRecord64>(1, ByteOrder.LittleEndian);
        Assert.Contains(payload, value => value != 0);
    }

    [Fact]
    public void Payloads_DifferBetweenByteOrders()
    {
        byte[] littleEndian = T2Payload.Create<ServiceRecord16Format, ServiceRecord16>(4, ByteOrder.LittleEndian);
        byte[] bigEndian = T2Payload.Create<ServiceRecord16Format, ServiceRecord16>(4, ByteOrder.BigEndian);
        Assert.NotEqual(littleEndian, bigEndian);
    }

    private static void AssertStrategiesAgree<TFormat, TRecord>()
        where TFormat : IRecordFormat<TRecord>
        where TRecord : unmanaged
    {
        const int count = RecordCount;

        byte[] littleEndian = T2Payload.Create<TFormat, TRecord>(count, ByteOrder.LittleEndian);
        byte[] bigEndian = T2Payload.Create<TFormat, TRecord>(count, ByteOrder.BigEndian);
        long expected = T2Payload.Fingerprint<TFormat, TRecord>(count);
        byte[] scratch = new byte[TFormat.SizeInBytes];

        // A fingerprint of zero would make every strategy look correct.
        Assert.NotEqual(0, expected);

        // S05/S06: the explicit byte orders have to decode the same records.
        Assert.Equal(expected, T2Strategies.Primitives<TFormat, TRecord>(littleEndian, count, ByteOrder.LittleEndian));
        Assert.Equal(expected, T2Strategies.Primitives<TFormat, TRecord>(bigEndian, count, ByteOrder.BigEndian));

        // S07-S09: native-order struct views over the little-endian image,
        // which every platform the PoC runs on implements as little endian.
        Assert.Equal(expected, T2Strategies.MemoryMarshalRead<TFormat, TRecord>(littleEndian, count));
        Assert.Equal(expected, T2Strategies.UnsafeReadUnaligned<TFormat, TRecord>(littleEndian, count));
        Assert.Equal(expected, T2Strategies.Cast<TFormat, TRecord>(littleEndian, count));

        // S01-S04: the stream baseline, including the bulk-region drain.
        using MemoryStreamSource source = MemoryStreamFactory.Create(littleEndian);
        using var reader = new BinaryReader(source.Stream);
        source.Rewind();
        Assert.Equal(expected, T2Strategies.BinaryReader<TFormat, TRecord>(reader, count, scratch));
    }
}