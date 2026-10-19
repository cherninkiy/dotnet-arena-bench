using System.Buffers;

using ArenaBench.Core;

using BenchmarkDotNet.Attributes;

namespace ArenaBench.Allocator.Bench;

/// <summary>
/// B01: access to an already allocated buffer through <see cref="Span{T}"/> -
/// the access axis of concept.md 5.2, with the allocator fixed as the only
/// variable. The buffers are allocated once in [GlobalSetup] and re-read every
/// iteration, so this case measures where the memory is, not how it was rented.
/// </summary>
/// <remarks>
/// The walk touches one byte per cache line (stride 16) and sums it: a
/// memory-bound pattern rather than a per-element ALU loop, which is what the
/// solver's scan does. The value is read back after the write, so the store
/// cannot be dropped.
/// </remarks>
public class AccessSpanBench
{
    private const int Stride = 16;

    private Arena _arena = null!;
    private IntPtr _arenaBuffer;
    private byte[] _managed = null!;
    private byte[]? _pooled;

    /// <summary>Buffer size in bytes (concept.md 5.2).</summary>
    [Params(32, 1024, 65536, 1048576)]
    public int SizeBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _arena = new Arena((nuint)SizeBytes + 64);
        _arenaBuffer = _arena.AllocPtr<byte>(SizeBytes);
        _managed = new byte[SizeBytes];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_pooled is not null)
        {
            ArrayPool<byte>.Shared.Return(_pooled);
        }

        _arena.Dispose();
    }

    /// <summary>B01 baseline: the same walk over a managed array.</summary>
    [Benchmark(Baseline = true)]
    public long ManagedArray_Span() => Walk(_managed);

    /// <summary>B01: the same walk over arena memory - one block, no GC object header.</summary>
    [Benchmark]
    public unsafe long Arena_Span() => Walk(new Span<byte>((void*)_arenaBuffer, SizeBytes));

    /// <summary>B01: the same walk over a pooled array (rented once, returned in cleanup).</summary>
    [Benchmark]
    public long ArrayPool_Span()
    {
        _pooled ??= ArrayPool<byte>.Shared.Rent(SizeBytes);
        return Walk(_pooled.AsSpan(0, SizeBytes));
    }

    private static long Walk(Span<byte> buffer)
    {
        long sum = 0;
        for (int index = 0; index < buffer.Length; index += Stride)
        {
            buffer[index] = (byte)index;
            sum += buffer[index];
        }

        return sum;
    }
}

/// <summary>
/// B02: the same access through <see cref="Memory{T}"/> instead of a span - the
/// view the BCL APIs ask for (ADR-001 D9). Every variant creates the view inside
/// the measured call and uses it immediately: an arena view must not outlive a
/// reset, which is the contract this case documents.
/// </summary>
/// <remarks>
/// For the arena the view is the per-allocation
/// <see cref="ArenaMemoryManager{T}"/> from <c>AllocMemory</c>, so the case also
/// prices the generation check the manager runs on every view access (one field
/// comparison, ADR-001 D9) against a plain <c>Memory&lt;T&gt;</c> wrapper.
/// </remarks>
public class AccessMemoryBench
{
    private const int Stride = 16;

    private Arena _arena = null!;
    private float[] _managed = null!;
    private int _elements;

    /// <summary>Buffer size in bytes (concept.md 5.2).</summary>
    [Params(32, 1024, 65536, 1048576)]
    public int SizeBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _elements = SizeBytes / sizeof(float);
        _managed = new float[_elements];
        _arena = new Arena((nuint)SizeBytes + 64);
    }

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    /// <summary>B02 baseline: a managed array behind a Memory&lt;float&gt;.</summary>
    [Benchmark(Baseline = true)]
    public double ManagedArray_Memory() => Walk(_managed.AsMemory());

    /// <summary>B02: an arena view, created and used inside the call (ADR-001 D9).</summary>
    [Benchmark]
    public double Arena_AllocMemory()
    {
        Memory<float> view = _arena.AllocMemory<float>(_elements);
        double sum = Walk(view);
        _arena.Reset(); // the view dies here and must not be touched again (D9)
        return sum;
    }

    /// <summary>B02: a pooled array behind a Memory&lt;float&gt; - the ArrayPool answer to the same need.</summary>
    [Benchmark]
    public double ArrayPool_Memory()
    {
        float[] buffer = ArrayPool<float>.Shared.Rent(_elements);
        try
        {
            return Walk(buffer.AsMemory(0, _elements));
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    /// <summary>B02: MemoryPool - the BCL pool whose owner hands out a Memory&lt;float&gt;, array-backed here.</summary>
    [Benchmark]
    public double MemoryPool_Memory()
    {
        using IMemoryOwner<float> owner = MemoryPool<float>.Shared.Rent(_elements);
        return Walk(owner.Memory.Slice(0, _elements));
    }

    private static double Walk(Memory<float> view)
    {
        Span<float> span = view.Span;
        double sum = 0;
        for (int index = 0; index < span.Length; index += Stride)
        {
            span[index] = index;
            sum += span[index];
        }

        return sum;
    }
}

/// <summary>
/// B03: AoS against SoA (concept.md 5.2) on both memory locations - a struct
/// array against eight flat arrays, each of them managed and in the arena. Both
/// layouts touch the same number of floats (fields x records); only the order of
/// the walk changes, which is the variable this case isolates.
/// </summary>
/// <remarks>
/// Arena spans cannot be fields, so the arena layouts are addressed through the
/// pointers the setup allocated - the same pattern as <c>ArenaGraph</c>, and the
/// only way to re-enter one block across benchmark iterations without bumping
/// the arena again.
/// </remarks>
public class AccessLayoutBench
{
    private const int FieldCount = 8;

    private Arena _arena = null!;
    private Record32[] _managedAoS = null!;
    private float[][] _managedSoA = null!;
    private IntPtr _arenaAoS;
    private IntPtr[] _arenaSoA = null!;

    /// <summary>Records per iteration: 1 024 x 32 B = 32 KiB, 65 536 x 32 B = 2 MiB.</summary>
    [Params(1024, 65536)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public unsafe void Setup()
    {
        _managedAoS = new Record32[RecordCount];
        _managedSoA = new float[FieldCount][];
        _arenaSoA = new IntPtr[FieldCount];

        // Two layouts share the block: the AoS array and the eight SoA arrays,
        // each RecordCount x 32 B, plus the alignment slack of nine allocations.
        _arena = new Arena(((nuint)RecordCount * (sizeof(float) * FieldCount) * 2) + 4096);
        _arenaAoS = _arena.AllocPtr<Record32>(RecordCount);
        Span<Record32> arenaAoS = new((void*)_arenaAoS, RecordCount);

        for (int field = 0; field < FieldCount; field++)
        {
            _arenaSoA[field] = _arena.AllocPtr<float>(RecordCount);
            _managedSoA[field] = new float[RecordCount];
        }

        for (int record = 0; record < RecordCount; record++)
        {
            // Deterministic contents: arena memory is not zeroed (ADR-001 D5), and
            // a case that sums whatever the OS left there would not be comparable.
            Fill(ref _managedAoS[record], record);
            Fill(ref arenaAoS[record], record);

            for (int field = 0; field < FieldCount; field++)
            {
                _managedSoA[field][record] = record + field;
                new Span<float>((void*)_arenaSoA[field], RecordCount)[record] = record + field;
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    private struct Record32
    {
        public float X, Y, Z, W, P, Q, R, S;
    }

    private static void Fill(ref Record32 record, float value)
    {
        record.X = value;
        record.Y = value + 1;
        record.Z = value + 2;
        record.W = value + 3;
        record.P = value + 4;
        record.Q = value + 5;
        record.R = value + 6;
        record.S = value + 7;
    }

    /// <summary>B03 baseline: AoS over a managed struct array.</summary>
    [Benchmark(Baseline = true)]
    public double AoS_Managed() => SumAoS(_managedAoS);

    /// <summary>B03: AoS in the arena - the struct array lives in one unmanaged block.</summary>
    [Benchmark]
    public unsafe double AoS_Arena() => SumAoS(new Span<Record32>((void*)_arenaAoS, RecordCount));

    /// <summary>B03: SoA over eight managed flat arrays.</summary>
    [Benchmark]
    public double SoA_Managed()
    {
        double sum = 0;
        for (int field = 0; field < FieldCount; field++)
        {
            float[] values = _managedSoA[field];
            for (int index = 0; index < values.Length; index++)
            {
                sum += values[index];
            }
        }

        return sum;
    }

    /// <summary>B03: SoA in the arena - one block per field, addressed through its pointer.</summary>
    [Benchmark]
    public unsafe double SoA_Arena()
    {
        double sum = 0;
        for (int field = 0; field < FieldCount; field++)
        {
            Span<float> values = new((void*)_arenaSoA[field], RecordCount);
            for (int index = 0; index < values.Length; index++)
            {
                sum += values[index];
            }
        }

        return sum;
    }

    private static double SumAoS(Span<Record32> records)
    {
        double sum = 0;
        for (int index = 0; index < records.Length; index++)
        {
            ref Record32 record = ref records[index];
            sum += record.X + record.Y + record.Z + record.W
                + record.P + record.Q + record.R + record.S;
        }

        return sum;
    }
}
