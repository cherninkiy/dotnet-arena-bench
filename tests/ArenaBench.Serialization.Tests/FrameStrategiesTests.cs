using ArenaBench.Core;

using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Sources;

namespace ArenaBench.Serialization.Tests;

/// <summary>
/// The T1 frame matrix: every reader has to reproduce the generator's
/// fingerprint, including the readers that never see a whole frame at once.
/// The payload is deliberately larger than the default pipe buffer, so frame
/// headers that straddle segments are part of the normal case.
/// </summary>
public sealed class FrameStrategiesTests
{
    private const int FrameCount = 256;

    private static readonly byte[] LittleEndian = FrameFormat.CreatePayload(FrameCount, ByteOrder.LittleEndian);
    private static readonly byte[] BigEndian = FrameFormat.CreatePayload(FrameCount, ByteOrder.BigEndian);

    [Fact]
    public void PayloadSizes_VaryAcrossTheFrames()
    {
        Assert.Equal(FrameFormat.MinPayloadSize, FrameFormat.PayloadSize(0));
        Assert.Equal(FrameFormat.MaxPayloadSize, FrameFormat.PayloadSize(7));
        Assert.Equal(FrameFormat.HeaderSize + FrameFormat.MaxPayloadSize, FrameFormat.FrameSize(7));

        int total = 0;
        for (int i = 0; i < FrameCount; i++)
        {
            total += FrameFormat.FrameSize(i);
        }

        Assert.Equal(total, FrameFormat.TotalSize(FrameCount));
        Assert.Equal(total, LittleEndian.Length);
    }

    [Fact]
    public void SpanReaders_AgreeWithTheFingerprint()
    {
        long expected = FrameFormat.Fingerprint(FrameCount);
        Assert.NotEqual(0, expected);

        Assert.Equal(expected, FrameStrategies.Primitives(LittleEndian, FrameCount, ByteOrder.LittleEndian));
        Assert.Equal(expected, FrameStrategies.Primitives(BigEndian, FrameCount, ByteOrder.BigEndian));
        Assert.Equal(expected, FrameStrategies.MemoryMarshalRead(LittleEndian, FrameCount));
        Assert.Equal(expected, FrameStrategies.UnsafeReadUnaligned(LittleEndian, FrameCount));
    }

    [Fact]
    public void BinaryReaderAndPipelines_AgreeWithTheFingerprint()
    {
        long expected = FrameFormat.Fingerprint(FrameCount);
        byte[] scratch = new byte[FrameFormat.MaxPayloadSize];

        using (var stream = new MemoryStream(LittleEndian))
        using (var reader = new BinaryReader(stream))
        {
            Assert.Equal(expected, FrameStrategies.BinaryReader(reader, FrameCount, scratch));
        }

        using (var stream = new MemoryStream(LittleEndian))
        {
            Assert.Equal(expected, FrameStrategies.Pipelines(stream, FrameCount));
        }
    }

    [Fact]
    public void Pipelines_HandlesHeadersSplitAcrossSegments()
    {
        using ChunkedStream chunked = ChunkedStreamFactory.Create(LittleEndian, maxChunk: 7);

        Assert.Equal(FrameFormat.Fingerprint(FrameCount), FrameStrategies.Pipelines(chunked, FrameCount));
    }

    [Fact]
    public void CarryReader_AgreesWithTheFingerprint()
    {
        long expected = FrameFormat.Fingerprint(FrameCount);

        using (ChunkedStream chunked = ChunkedStreamFactory.Create(LittleEndian))
        {
            byte[] managedCarry = new byte[256];
            Assert.Equal(expected, FrameStrategies.WithCarry(chunked, managedCarry, FrameCount));
        }

        using ChunkedStream arenaCarryStream = ChunkedStreamFactory.Create(LittleEndian);
        using var carryArena = new Arena(320);
        Span<byte> carry = carryArena.AllocSpan<byte>(256);

        Assert.Equal(expected, FrameStrategies.WithCarry(arenaCarryStream, carry, FrameCount));
    }

    [Fact]
    public void CarryBuffer_TooSmallForOneFrame_Throws()
    {
        using ChunkedStream chunked = ChunkedStreamFactory.Create(LittleEndian);

        Assert.Throws<ArgumentException>(() => FrameStrategies.WithCarry(chunked, new byte[16], 1));
    }
}