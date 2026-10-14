using System.Text;

using ArenaBench.Core;

using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Sources;
using BenchmarkDotNet.Attributes;

namespace ArenaBench.Serialization.Bench;

/// <summary>
/// T1 stream benchmarks (concept.md 5.1, День 2): variable-length frames read
/// through different readers and different memories.
/// </summary>
/// <remarks>
/// V01-V04 fix the source and vary the reader (BinaryReader, Span.Slice,
/// Unsafe.ReadUnaligned, Pipelines); V05/V06 put the same frames into arena
/// memory at two alignments; V07/V08 are the streaming-with-carry pair over one
/// chunked source, so only the carry buffer differs (managed array vs arena).
/// </remarks>
public class T1StreamBench
{
    /// <summary>Frames per payload: 256 frames of 24..136 B, about 19 KiB.</summary>
    private const int FrameCount = 256;

    /// <summary>Carry buffer size - one frame (136 B) with room to spare.</summary>
    private const int CarrySize = 256;

    private byte[] _payload = null!;
    private byte[] _scratch = null!;
    private byte[] _managedCarry = null!;
    private MemoryStream _stream = null!;
    private BinaryReader _reader = null!;
    private ChunkedStream _chunked = null!;
    private ArenaPayloadSource _arenaTight = null!;
    private ArenaPayloadSource _arenaAlign64 = null!;
    private Arena _carryArena = null!;

    [GlobalSetup]
    public void Setup()
    {
        _payload = FrameFormat.CreatePayload(FrameCount, ByteOrder.LittleEndian);
        _scratch = new byte[FrameFormat.MaxPayloadSize];
        _managedCarry = new byte[CarrySize];

        _stream = new MemoryStream(_payload, writable: false);
        _reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
        _chunked = ChunkedStreamFactory.Create(_payload);

        _arenaTight = ArenaPayloadFactory.Create(_payload, alignment: 1);
        _arenaAlign64 = ArenaPayloadFactory.Create(_payload, alignment: 64);
        _carryArena = new Arena((nuint)(CarrySize + 64), defaultAlign: 16);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader.Dispose();
        _stream.Dispose();
        _chunked.Dispose();
        _arenaTight.Dispose();
        _arenaAlign64.Dispose();
        _carryArena.Dispose();
    }

    /// <summary>V01: BinaryReader over MemoryStream - the frame baseline.</summary>
    [Benchmark(Baseline = true)]
    public long V01_Stream_BinaryReader()
    {
        _stream.Position = 0;
        return FrameStrategies.BinaryReader(_reader, FrameCount, _scratch);
    }

    /// <summary>V02: Span.Slice + BinaryPrimitives over the byte[].</summary>
    [Benchmark]
    public long V02_Span_Primitives() => FrameStrategies.Primitives(_payload, FrameCount, ByteOrder.LittleEndian);

    /// <summary>V03: header struct view via Unsafe.ReadUnaligned over the byte[].</summary>
    [Benchmark]
    public long V03_Span_UnsafeReadUnaligned() => FrameStrategies.UnsafeReadUnaligned(_payload, FrameCount);

    /// <summary>V04: PipeReader/SequenceReader over the stream.</summary>
    [Benchmark]
    public long V04_Pipelines()
    {
        _stream.Position = 0;
        return FrameStrategies.Pipelines(_stream, FrameCount);
    }

    /// <summary>V05: arena, tight (align 1), field by field.</summary>
    [Benchmark]
    public long V05_Arena_Tight_Primitives() =>
        FrameStrategies.Primitives(_arenaTight.Span, FrameCount, ByteOrder.LittleEndian);

    /// <summary>V06: arena, align 64 (cache line), field by field.</summary>
    [Benchmark]
    public long V06_Arena_Align64_Primitives() =>
        FrameStrategies.Primitives(_arenaAlign64.Span, FrameCount, ByteOrder.LittleEndian);

    /// <summary>V07: chunked source, carry buffer in a managed array.</summary>
    [Benchmark]
    public long V07_Carry_ManagedBuffer()
    {
        _chunked.Position = 0;
        return FrameStrategies.WithCarry(_chunked, _managedCarry, FrameCount);
    }

    /// <summary>V08: chunked source, carry buffer in arena memory (Reset + bump per iteration).</summary>
    [Benchmark]
    public long V08_Carry_ArenaBuffer()
    {
        _chunked.Position = 0;
        _carryArena.Reset();
        Span<byte> carry = _carryArena.AllocSpan<byte>(CarrySize);
        return FrameStrategies.WithCarry(_chunked, carry, FrameCount);
    }
}