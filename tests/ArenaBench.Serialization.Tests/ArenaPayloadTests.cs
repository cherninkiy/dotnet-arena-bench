using System.Buffers;
using System.Text.Json;

using ArenaBench.Core;

using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Sources;
using ArenaBench.Serialization.Core.Structures;
using ArenaBench.Serialization.Core.Text;

namespace ArenaBench.Serialization.Tests;

/// <summary>
/// The arena-backed source and the two arena bridges the S10-S22 and C03/C04
/// benches rely on: alignment, views, chunked delivery and the buffer writer.
/// </summary>
public sealed class ArenaPayloadTests
{
    private const int RecordCount = 4;

    private static readonly byte[] Payload =
        T2Payload.Create<VideoRecord64Format, VideoRecord64>(RecordCount, ByteOrder.LittleEndian);

    /// <summary>
    /// The alignment axis is only an axis if the three cases really land on
    /// different addresses: the probe allocation inside the payload source puts
    /// them at modulo 64 = 1 / 16 / 0. Without it every case would be 64-byte
    /// aligned, because the arena block base is, and S10/S11/S12 would measure
    /// one and the same layout.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(16, 16)]
    [InlineData(64, 0)]
    public void Block_LandsOnTheRequestedAlignment(int alignment, int expectedModulo64)
    {
        using ArenaPayloadSource source = ArenaPayloadFactory.Create(Payload, alignment);

        Assert.Equal(alignment, source.Alignment);
        Assert.Equal(Payload.Length, source.Length);
        Assert.Equal(0, source.Buffer.ToInt64() % alignment);
        Assert.Equal(expectedModulo64, source.Buffer.ToInt64() % 64);
        Assert.Equal(Payload, source.Span.ToArray());
    }

    [Fact]
    public void Alignments_ProduceDistinctAddresses()
    {
        using ArenaPayloadSource tight = ArenaPayloadFactory.Create(Payload, alignment: 1);
        using ArenaPayloadSource align16 = ArenaPayloadFactory.Create(Payload, alignment: 16);
        using ArenaPayloadSource align64 = ArenaPayloadFactory.Create(Payload, alignment: 64);

        long[] offsets = [tight.Buffer.ToInt64() % 64, align16.Buffer.ToInt64() % 64, align64.Buffer.ToInt64() % 64];

        Assert.Equal(3, offsets.Distinct().Count());
    }

    [Fact]
    public void Views_ReadTheSameRecords()
    {
        long expected = T2Payload.Fingerprint<VideoRecord64Format, VideoRecord64>(RecordCount);
        byte[] scratch = new byte[VideoRecord64Format.SizeInBytes];

        using ArenaPayloadSource source = ArenaPayloadFactory.Create(Payload, alignment: 16);

        Assert.Equal(expected, T2Strategies.Primitives<VideoRecord64Format, VideoRecord64>(
            source.Span, RecordCount, ByteOrder.LittleEndian));
        Assert.Equal(expected, T2Strategies.Cast<VideoRecord64Format, VideoRecord64>(source.Span, RecordCount));

        using var reader = new BinaryReader(source.Stream);
        source.Rewind();
        Assert.Equal(expected, T2Strategies.BinaryReader<VideoRecord64Format, VideoRecord64>(
            reader, RecordCount, scratch));
    }

    [Fact]
    public void TightBlock_ReadsTheSameRecords()
    {
        // The unaligned case is what S10/V05 measure, so it has to decode the very
        // same records as the aligned one - on x64 the penalty is performance, not
        // correctness.
        long expected = T2Payload.Fingerprint<VideoRecord64Format, VideoRecord64>(RecordCount);

        using ArenaPayloadSource source = ArenaPayloadFactory.Create(Payload, alignment: 1);

        Assert.Equal(expected, T2Strategies.Primitives<VideoRecord64Format, VideoRecord64>(
            source.Span, RecordCount, ByteOrder.LittleEndian));
        Assert.Equal(expected, T2Strategies.MemoryMarshalRead<VideoRecord64Format, VideoRecord64>(
            source.Span, RecordCount));
        Assert.Equal(expected, T2Strategies.UnsafeReadUnaligned<VideoRecord64Format, VideoRecord64>(
            source.Span, RecordCount));
    }

    [Fact]
    public void ChunkedStream_RejectsInvalidArguments()
    {
        using ChunkedStream chunked = ChunkedStreamFactory.Create(Payload, maxChunk: 5);
        byte[] buffer = new byte[16];

        Assert.Throws<ArgumentOutOfRangeException>(() => chunked.Read(buffer, 0, -1));
        Assert.Throws<ArgumentException>(() => chunked.Read(buffer, 8, 16));
    }

    [Fact]
    public void ChunkedStream_CapsEveryRead()
    {
        using ChunkedStream chunked = ChunkedStreamFactory.Create(Payload, maxChunk: 5);

        byte[] buffer = new byte[64];
        Assert.Equal(5, chunked.Read(buffer));
        Assert.Equal(Payload.Length, chunked.Length);
    }

    [Fact]
    public void ArenaBufferWriter_MatchesArrayBufferWriter()
    {
        TextMessage message = TextMessage.Create(3);

        var managed = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(managed))
        {
            JsonPayload.WriteMessage(writer, message);
        }

        using var arena = new Arena(4096);
        using var arenaWriter = new ArenaBufferWriter(arena, 4096);
        using (var writer = new Utf8JsonWriter(arenaWriter))
        {
            JsonPayload.WriteMessage(writer, message);
        }

        Assert.Equal(managed.WrittenSpan.ToArray(), arenaWriter.WrittenSpan.ToArray());
        Assert.Equal(managed.WrittenCount, arenaWriter.WrittenCount);
    }

    [Fact]
    public void ArenaBufferWriter_ResetStartsANewMessage()
    {
        using var arena = new Arena(4096);
        using var arenaWriter = new ArenaBufferWriter(arena, 4096);

        arenaWriter.GetSpan(16);
        arenaWriter.Advance(16);
        Assert.Equal(16, arenaWriter.WrittenCount);

        arenaWriter.Reset();
        Assert.Equal(0, arenaWriter.WrittenCount);
        Assert.True(arenaWriter.WrittenSpan.IsEmpty);
    }

    [Fact]
    public void ArenaBufferWriter_HintBeyondCapacity_Throws()
    {
        using var arena = new Arena(64);
        using var arenaWriter = new ArenaBufferWriter(arena, 64);

        Assert.Throws<InvalidOperationException>(() => arenaWriter.GetSpan(128));
    }
}