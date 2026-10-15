using System.Buffers;
using System.Text;

using ArenaBench.Core;

using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Sources;
using ArenaBench.Serialization.Core.Structures;
using BenchmarkDotNet.Attributes;

namespace ArenaBench.Serialization.Bench;

/// <summary>
/// T2 structured benchmarks (concept.md 5.1, День 1): one concrete class per
/// fixed-layout record, one benchmark per source x strategy cell.
/// </summary>
/// <remarks>
/// The payload holds <see cref="T2Payload.DefaultRecordCount"/> records built
/// with the fixed seed semantics of concept.md 7.4, so every strategy of a
/// class reads exactly the same bytes. Each case returns the summed record
/// fingerprint: results stay comparable across strategies and the JIT cannot
/// drop the reads as dead code.
/// <para>
/// S01-S04 keep the source axis (MemoryStream, FileStream, NetworkStream,
/// UnmanagedMemoryStream) and read the payload through a stream; S05-S09 keep
/// the parsing axis and read a managed <c>byte[]</c>, so in each group the
/// other axis is fixed.
/// </para>
/// <para>
/// Diagnosers come from <see cref="BenchmarkConfig"/> alone: the config is
/// applied to every benchmark in the assembly, so no attribute is repeated
/// here.
/// </para>
/// <para>
/// Arena cases are combined deliberately, one variable at a time: S10/S11/S12
/// share the strategy and vary only the alignment (1/16/64, which the payload
/// source makes physical by placing the payload behind a one-byte probe), S19
/// reuses the align-16 block of S11 so that only the byte order differs, and
/// S13-S18 read the align-16 block as well.
/// </para>
/// </remarks>
public abstract class T2StructuredBench<TFormat, TRecord>
    where TFormat : IRecordFormat<TRecord>
    where TRecord : unmanaged
{
    /// <summary>Records per payload - 256 x 16..256 B = 4..64 KiB.</summary>
    protected const int RecordCount = T2Payload.DefaultRecordCount;

    private byte[] _littleEndianPayload = null!;
    private byte[] _bigEndianPayload = null!;
    private byte[] _scratch = null!;

    private MemoryStreamSource _memory = null!;
    private FileStreamSource _file = null!;
    private NetworkStreamSource _network = null!;
    private UnmanagedStreamSource _unmanaged = null!;

    private BinaryReader _memoryReader = null!;
    private BinaryReader _fileReader = null!;
    private BinaryReader _networkReader = null!;
    private BinaryReader _unmanagedReader = null!;

    private ArenaPayloadSource _arenaTight = null!;
    private ArenaPayloadSource _arenaAlign16 = null!;
    private ArenaPayloadSource _arenaAlign64 = null!;
    private ArenaPayloadSource _arenaBigEndian = null!;
    private BinaryReader _arenaReader = null!;

    private Arena _arenaMemoryOwner = null!;
    private Memory<byte> _arenaMemory;
    private Arena _zeroArena = null!;

    [GlobalSetup]
    public void Setup()
    {
        _littleEndianPayload = T2Payload.Create<TFormat, TRecord>(RecordCount, ByteOrder.LittleEndian);
        _bigEndianPayload = T2Payload.Create<TFormat, TRecord>(RecordCount, ByteOrder.BigEndian);
        _scratch = new byte[TFormat.SizeInBytes];

        _memory = MemoryStreamFactory.Create(_littleEndianPayload);
        _file = FileStreamFactory.Create(_littleEndianPayload);
        _network = NetworkStreamFactory.CreateLoopback(_littleEndianPayload);
        _unmanaged = UnmanagedStreamFactory.Create(_littleEndianPayload);

        _memoryReader = new BinaryReader(_memory.Stream, Encoding.UTF8, leaveOpen: true);
        _fileReader = new BinaryReader(_file.Stream, Encoding.UTF8, leaveOpen: true);
        _networkReader = new BinaryReader(_network.Stream, Encoding.UTF8, leaveOpen: true);
        _unmanagedReader = new BinaryReader(_unmanaged.Stream, Encoding.UTF8, leaveOpen: true);

        // S10-S20: the very same bytes in arena memory. The payload source
        // places the payload behind a one-byte probe, so align 1 really lands on
        // offset 1 (unaligned), align 16 on offset 16 and align 64 on offset 64.
        _arenaTight = ArenaPayloadFactory.Create(_littleEndianPayload, alignment: 1);
        _arenaAlign16 = ArenaPayloadFactory.Create(_littleEndianPayload, alignment: 16);
        _arenaAlign64 = ArenaPayloadFactory.Create(_littleEndianPayload, alignment: 64);

        // Same alignment as S11 on purpose: S19 must differ by byte order alone.
        _arenaBigEndian = ArenaPayloadFactory.Create(_bigEndianPayload, alignment: 16);
        _arenaReader = new BinaryReader(_arenaAlign16.Stream, Encoding.UTF8, leaveOpen: true);

        // S16: the Memory<byte> view of the payload (ADR-001 D9) and the arena
        // the zeroed variant (S20) bumps through, reset per iteration.
        _arenaMemoryOwner = new Arena((nuint)_littleEndianPayload.Length, defaultAlign: 16);
        _arenaMemory = _arenaMemoryOwner.AllocMemory<byte>(_littleEndianPayload.Length);
        _littleEndianPayload.CopyTo(_arenaMemory.Span);
        _zeroArena = new Arena((nuint)(_littleEndianPayload.Length + 64));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _memoryReader.Dispose();
        _fileReader.Dispose();
        _networkReader.Dispose();
        _unmanagedReader.Dispose();

        _memory.Dispose();
        _file.Dispose();
        _network.Dispose();
        _unmanaged.Dispose();

        _arenaReader.Dispose();
        _arenaTight.Dispose();
        _arenaAlign16.Dispose();
        _arenaAlign64.Dispose();
        _arenaBigEndian.Dispose();
        _arenaMemoryOwner.Dispose();
        _zeroArena.Dispose();
    }

    /// <summary>S01: BinaryReader over MemoryStream - the managed baseline.</summary>
    [Benchmark(Baseline = true)]
    public long S01_MemoryStream_BinaryReader()
    {
        _memory.Rewind();
        return T2Strategies.BinaryReader<TFormat, TRecord>(_memoryReader, RecordCount, _scratch);
    }

    /// <summary>S02: BinaryReader over FileStream.</summary>
    [Benchmark]
    public long S02_FileStream_BinaryReader()
    {
        _file.Rewind();
        return T2Strategies.BinaryReader<TFormat, TRecord>(_fileReader, RecordCount, _scratch);
    }

    /// <summary>S03: BinaryReader over a loopback NetworkStream.</summary>
    [Benchmark]
    public long S03_NetworkStream_BinaryReader()
    {
        _network.Rewind();
        return T2Strategies.BinaryReader<TFormat, TRecord>(_networkReader, RecordCount, _scratch);
    }

    /// <summary>S04: BinaryReader over UnmanagedMemoryStream - the unmanaged-to-BCL bridge.</summary>
    [Benchmark]
    public long S04_UnmanagedMemoryStream_BinaryReader()
    {
        _unmanaged.Rewind();
        return T2Strategies.BinaryReader<TFormat, TRecord>(_unmanagedReader, RecordCount, _scratch);
    }

    /// <summary>S05: BinaryPrimitives, explicit little endian, field by field.</summary>
    [Benchmark]
    public long S05_Span_BinaryPrimitives_LE() =>
        T2Strategies.Primitives<TFormat, TRecord>(_littleEndianPayload, RecordCount, ByteOrder.LittleEndian);

    /// <summary>S06: BinaryPrimitives, explicit big endian over the big-endian payload.</summary>
    [Benchmark]
    public long S06_Span_BinaryPrimitives_BE() =>
        T2Strategies.Primitives<TFormat, TRecord>(_bigEndianPayload, RecordCount, ByteOrder.BigEndian);

    /// <summary>S07: MemoryMarshal.Read, one typed struct copy per record.</summary>
    [Benchmark]
    public long S07_Span_MemoryMarshalRead() =>
        T2Strategies.MemoryMarshalRead<TFormat, TRecord>(_littleEndianPayload, RecordCount);

    /// <summary>S08: Unsafe.ReadUnaligned, one typed struct read per record without a copy.</summary>
    [Benchmark]
    public long S08_Span_UnsafeReadUnaligned() =>
        T2Strategies.UnsafeReadUnaligned<TFormat, TRecord>(_littleEndianPayload, RecordCount);

    /// <summary>S09: MemoryMarshal.Cast, the bulk struct view without per-record copies.</summary>
    [Benchmark]
    public long S09_Span_Cast() =>
        T2Strategies.Cast<TFormat, TRecord>(_littleEndianPayload, RecordCount);

    /// <summary>S10: arena, tight allocation (align 1), field by field.</summary>
    [Benchmark]
    public long S10_Arena_Tight_Primitives() =>
        T2Strategies.Primitives<TFormat, TRecord>(_arenaTight.Span, RecordCount, ByteOrder.LittleEndian);

    /// <summary>S11: arena, align 16, field by field - the same work as S05, different memory.</summary>
    [Benchmark]
    public long S11_Arena_Align16_Primitives() =>
        T2Strategies.Primitives<TFormat, TRecord>(_arenaAlign16.Span, RecordCount, ByteOrder.LittleEndian);

    /// <summary>S12: arena, align 64 (cache line), field by field.</summary>
    [Benchmark]
    public long S12_Arena_Align64_Primitives() =>
        T2Strategies.Primitives<TFormat, TRecord>(_arenaAlign64.Span, RecordCount, ByteOrder.LittleEndian);

    /// <summary>S13: arena, one typed struct copy per record via MemoryMarshal.Read.</summary>
    [Benchmark]
    public long S13_Arena_MemoryMarshalRead() =>
        T2Strategies.MemoryMarshalRead<TFormat, TRecord>(_arenaAlign16.Span, RecordCount);

    /// <summary>S14: arena, one typed struct read per record via Unsafe.ReadUnaligned.</summary>
    [Benchmark]
    public long S14_Arena_UnsafeReadUnaligned() =>
        T2Strategies.UnsafeReadUnaligned<TFormat, TRecord>(_arenaAlign16.Span, RecordCount);

    /// <summary>S15: arena, bulk struct view via MemoryMarshal.Cast.</summary>
    [Benchmark]
    public long S15_Arena_Cast() =>
        T2Strategies.Cast<TFormat, TRecord>(_arenaAlign16.Span, RecordCount);

    /// <summary>S16: arena, reached through Memory&lt;byte&gt; (ArenaMemoryManager, ADR-001 D9).</summary>
    [Benchmark]
    public long S16_ArenaMemory_Span_Primitives() =>
        T2Strategies.Primitives<TFormat, TRecord>(_arenaMemory.Span, RecordCount, ByteOrder.LittleEndian);

    /// <summary>S17: arena through the BCL bridge - BinaryReader over UnmanagedMemoryStream.</summary>
    [Benchmark]
    public long S17_Arena_UnmanagedStream_BinaryReader()
    {
        _arenaAlign16.Rewind();
        return T2Strategies.BinaryReader<TFormat, TRecord>(_arenaReader, RecordCount, _scratch);
    }

    /// <summary>S18: arena bytes staged into a pooled buffer, then parsed - handing unmanaged memory to managed code.</summary>
    [Benchmark]
    public long S18_Arena_StagedCopy_ArrayPool()
    {
        byte[] staging = ArrayPool<byte>.Shared.Rent(_arenaAlign16.Length);
        try
        {
            _arenaAlign16.Span.CopyTo(staging);
            return T2Strategies.Primitives<TFormat, TRecord>(
                staging.AsSpan(0, _arenaAlign16.Length), RecordCount, ByteOrder.LittleEndian);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(staging);
        }
    }

    /// <summary>S19: arena, explicit big endian - the swap cost on unmanaged memory.</summary>
    [Benchmark]
    public long S19_Arena_BigEndian_Primitives() =>
        T2Strategies.Primitives<TFormat, TRecord>(_arenaBigEndian.Span, RecordCount, ByteOrder.BigEndian);

    /// <summary>S20: arena region that is zeroed and filled per iteration (ADR-001 D5, Reset + bump).</summary>
    [Benchmark]
    public long S20_Arena_AllocZeroedCopy()
    {
        _zeroArena.Reset();
        Span<byte> target = _zeroArena.AllocSpanZeroed<byte>(_arenaAlign16.Length);
        _arenaAlign16.Span.CopyTo(target);
        return T2Strategies.Primitives<TFormat, TRecord>(target, RecordCount, ByteOrder.LittleEndian);
    }
}

/// <summary>T2 cells for the 16 B service record.</summary>
public class T2ServiceRecord16Bench : T2StructuredBench<ServiceRecord16Format, ServiceRecord16>
{
}

/// <summary>T2 cells for the 32 B commodity record.</summary>
public class T2Commodity32Bench : T2StructuredBench<Commodity32Format, Commodity32>
{
}

/// <summary>T2 cells for the 64 B video record.</summary>
public class T2VideoRecord64Bench : T2StructuredBench<VideoRecord64Format, VideoRecord64>
{
}

/// <summary>T2 cells for the 128 B extended record.</summary>
public class T2ExtendedRecord128Bench : T2StructuredBench<ExtendedRecord128Format, ExtendedRecord128>
{
}

/// <summary>T2 cells for the 256 B multi-field record.</summary>
public class T2MultiFieldRecord256Bench : T2StructuredBench<MultiFieldRecord256Format, MultiFieldRecord256>
{
}

/// <summary>
/// S21/S22: the variable-length end of the T2 axis - frame images instead of
/// fixed-stride records. Frames do not depend on the size ladder, so they get
/// their own class instead of being repeated once per ladder class.
/// </summary>
public class T2VariableLengthBench
{
    /// <summary>Frames per payload: 256 frames of 24..136 B, about 19 KiB.</summary>
    private const int FrameCount = 256;

    private byte[] _scratch = null!;
    private ArenaPayloadSource _arena = null!;
    private BinaryReader _arenaReader = null!;

    [GlobalSetup]
    public void Setup()
    {
        byte[] payload = FrameFormat.CreatePayload(FrameCount, ByteOrder.LittleEndian);
        _scratch = new byte[FrameFormat.MaxPayloadSize];

        _arena = ArenaPayloadFactory.Create(payload, alignment: 16);
        _arenaReader = new BinaryReader(_arena.Stream, Encoding.UTF8, leaveOpen: true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _arenaReader.Dispose();
        _arena.Dispose();
    }

    /// <summary>S21: frames walked field by field over arena memory.</summary>
    [Benchmark]
    public long S21_Arena_VariableLength_Primitives() =>
        FrameStrategies.Primitives(_arena.Span, FrameCount, ByteOrder.LittleEndian);

    /// <summary>S22: frames read through the arena's UnmanagedMemoryStream.</summary>
    [Benchmark]
    public long S22_Arena_VariableLength_BinaryReader()
    {
        _arena.Rewind();
        return FrameStrategies.BinaryReader(_arenaReader, FrameCount, _scratch);
    }
}