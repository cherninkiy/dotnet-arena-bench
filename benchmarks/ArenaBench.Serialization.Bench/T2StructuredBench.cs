using System.Text;

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
/// </remarks>
[MemoryDiagnoser]
[ThreadingDiagnoser]
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