using System.Buffers;
using System.Runtime.InteropServices;

using ArenaBench.Allocator.Core.Solver;
using ArenaBench.Core;

using BenchmarkDotNet.Attributes;

namespace ArenaBench.Allocator.Bench;

/// <summary>
/// A01: one buffer, allocated and released once per operation, across the four
/// sizes of concept.md 5.2 (32 B, 1 KiB, 64 KiB, 1 MiB) and the allocators of
/// concept.md 5.2 (Arena, ArrayPool, NativeMemory, T[]). The point of the case
/// is the fixed cost of a single rent: the arena bumps an offset, ArrayPool goes
/// through a bucket, NativeMemory calls into the CRT, and `new T[]` adds a
/// zeroing pass plus a GC-visible object.
/// </summary>
/// <remarks>
/// The arena lives across iterations ([GlobalSetup]/[GlobalCleanup]) and is
/// reset per operation, so every variant reuses its storage - the comparison is
/// apples-to-apples. `stackalloc` is absent on purpose: a 1 MiB frame would kill
/// the runner, and A03 covers the small sizes where stackalloc is a real option.
/// Diagnosers come from <see cref="BenchmarkConfig"/>.
/// </remarks>
public class AllocSingleBench
{
    private Arena _arena = null!;

    /// <summary>Allocation size in bytes (concept.md 5.2).</summary>
    [Params(32, 1024, 65536, 1048576)]
    public int SizeBytes { get; set; }

    [GlobalSetup]
    public void Setup() => _arena = new Arena((nuint)SizeBytes + 64);

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    /// <summary>A01 baseline: the pooled managed array.</summary>
    [Benchmark(Baseline = true)]
    public int ArrayPool_RentReturn()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(SizeBytes);
        buffer[0] = 1;
        int length = buffer.Length; // read before Return: the array goes back to the pool
        ArrayPool<byte>.Shared.Return(buffer);
        return length;
    }

    /// <summary>A01: arena bump plus a reset - no free list, no header, no zeroing.</summary>
    [Benchmark]
    public long Arena_AllocReset()
    {
        Span<byte> span = _arena.AllocSpan<byte>(SizeBytes);
        span[0] = 1;
        long used = (long)_arena.UsedBytes;
        _arena.Reset();
        return used;
    }

    /// <summary>A01: NativeMemory - the unmanaged baseline without an arena on top.</summary>
    [Benchmark]
    public unsafe int NativeMemory_AlignedAllocFree()
    {
        void* block = NativeMemory.AlignedAlloc((nuint)SizeBytes, 64);
        try
        {
            ((byte*)block)[0] = 1;
        }
        finally
        {
            NativeMemory.AlignedFree(block);
        }

        return SizeBytes;
    }

    /// <summary>A01: a plain managed array - allocation plus CLR zeroing plus GC.</summary>
    [Benchmark]
    public int ManagedArray_New()
    {
        byte[] buffer = new byte[SizeBytes];
        buffer[0] = 1;
        return buffer.Length;
    }
}

/// <summary>
/// A02: a batch of buffers, then one rollback - the key case of ADR-001 D7
/// (concept.md 5.2: "batch alloc / Reset"). The arena bumps N times and resets
/// once; the pools have to give every buffer back. Sizes stay at or below
/// 64 KiB because the batch multiplies them: 256 x 64 KiB is the 16 MiB block
/// the widest case needs.
/// </summary>
/// <remarks>
/// The buffers are <c>float</c>, not <c>byte</c>: that is the shape the solver's
/// scratch has, and it is what lets <see cref="PathPoolMock"/> (whose bags hold
/// <c>int[]</c> and <c>float[]</c>, concept.md 5.2) take part in the A series.
/// <see cref="SizeBytes"/> keeps the byte sizes of the concept; the element
/// count follows from it.
/// </remarks>
public class AllocBatchBench
{
    private Arena _arena = null!;
    private PathPoolMock _pool = null!;
    private int _elements;

    // Reused holder for the pooled variants: allocating a fresh array of
    // references per iteration would put harness garbage into the measurement
    // the arena does not have (the same reason the arena lives in GlobalSetup).
    private float[][] _holders = null!;

    /// <summary>Size of one buffer in bytes (concept.md 5.2).</summary>
    [Params(32, 1024, 65536)]
    public int SizeBytes { get; set; }

    /// <summary>Buffers per batch.</summary>
    [Params(32, 256)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _elements = SizeBytes / sizeof(float);
        _holders = new float[Count][];

        // The pool lives across iterations for the same reason the arena does:
        // the case compares steady states, not the first-call allocation.
        _pool = new PathPoolMock();
        _arena = new Arena(((nuint)_elements * (nuint)Count * sizeof(float)) + 4096);
    }

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    /// <summary>A02 baseline: the incumbent pool from netgraph-optimizer.</summary>
    [Benchmark(Baseline = true)]
    public long PathPool_BatchRentReturn()
    {
        for (int i = 0; i < Count; i++)
        {
            _holders[i] = _pool.RentFloat(_elements);
            _holders[i][0] = i;
        }

        long capacity = 0;
        for (int i = 0; i < Count; i++)
        {
            capacity += _holders[i].Length;
            _pool.ReturnFloat(_holders[i]);
        }

        return capacity;
    }

    /// <summary>A02: N bumps, one O(1) reset (ADR-001 D7).</summary>
    [Benchmark]
    public long Arena_BatchThenReset()
    {
        for (int i = 0; i < Count; i++)
        {
            Span<float> buffer = _arena.AllocSpan<float>(_elements);
            buffer[0] = i;
        }

        long used = (long)_arena.UsedBytes;
        _arena.Reset();
        return used;
    }

    /// <summary>A02: the BCL pool, N rents and N returns.</summary>
    [Benchmark]
    public long ArrayPool_BatchRentReturn()
    {
        for (int i = 0; i < Count; i++)
        {
            _holders[i] = ArrayPool<float>.Shared.Rent(_elements);
            _holders[i][0] = i;
        }

        long capacity = 0;
        for (int i = 0; i < Count; i++)
        {
            capacity += _holders[i].Length;
            ArrayPool<float>.Shared.Return(_holders[i]);
        }

        return capacity;
    }

    /// <summary>A02: NativeMemory without an arena - every buffer is a CRT call pair.</summary>
    [Benchmark]
    public unsafe long NativeMemory_BatchAllocFree()
    {
        void** blocks = stackalloc void*[Count];

        for (int i = 0; i < Count; i++)
        {
            blocks[i] = NativeMemory.AlignedAlloc((nuint)(_elements * sizeof(float)), 64);
            ((float*)blocks[i])[0] = i;
        }

        long bytes = 0;
        for (int i = 0; i < Count; i++)
        {
            bytes += _elements * sizeof(float);
            NativeMemory.AlignedFree(blocks[i]);
        }

        return bytes;
    }
}

/// <summary>
/// A03: N small records - the "alloc/free x N (small)" row of concept.md 11.2.
/// One bump per record is nearly free, which is the arena's whole claim; the
/// variants are what it is measured against: N pool round trips, N CRT call
/// pairs, and one block out of which the records are sliced (the arena's own
/// shape, but with a managed array or the stack as the block).
/// </summary>
public class AllocChurnBench
{
    private Arena _arena = null!;
    private byte[][] _holders = null!;
    private IntPtr[] _blocks = null!;

    /// <summary>Size of one record in bytes.</summary>
    [Params(32, 64, 256)]
    public int RecordBytes { get; set; }

    /// <summary>Records per iteration.</summary>
    [Params(64, 256)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _arena = new Arena((nuint)(RecordBytes * Count) + 4096);
        _holders = new byte[Count][];
        _blocks = new IntPtr[Count];
    }

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    /// <summary>A03 baseline: N rents and N returns.</summary>
    [Benchmark(Baseline = true)]
    public long ArrayPool_NRentReturn()
    {
        for (int i = 0; i < Count; i++)
        {
            _holders[i] = ArrayPool<byte>.Shared.Rent(RecordBytes);
            _holders[i][0] = (byte)i;
        }

        long total = 0;
        for (int i = 0; i < Count; i++)
        {
            total += _holders[i].Length;
            ArrayPool<byte>.Shared.Return(_holders[i]);
        }

        return total;
    }

    /// <summary>A03: N bumps, one reset - no per-record bookkeeping at all.</summary>
    [Benchmark]
    public long Arena_BumpN()
    {
        for (int i = 0; i < Count; i++)
        {
            Span<byte> record = _arena.AllocSpan<byte>(RecordBytes);
            record[0] = (byte)i;
        }

        long used = (long)_arena.UsedBytes;
        _arena.Reset();
        return used;
    }

    /// <summary>A03: NativeMemory per record - N aligned CRT allocations and frees.</summary>
    [Benchmark]
    public unsafe long NativeMemory_NAllocFree()
    {
        for (int i = 0; i < Count; i++)
        {
            _blocks[i] = (IntPtr)NativeMemory.AlignedAlloc((nuint)RecordBytes, 64);
            ((byte*)_blocks[i])[0] = (byte)i;
        }

        long total = 0;
        for (int i = 0; i < Count; i++)
        {
            total += RecordBytes;
            NativeMemory.AlignedFree((void*)_blocks[i]);
        }

        return total;
    }

    /// <summary>A03: one managed array, records sliced out of it - the CLR zeroes the whole block.</summary>
    [Benchmark]
    public long ManagedArray_SingleBlock()
    {
        byte[] block = new byte[RecordBytes * Count];
        long total = 0;
        for (int i = 0; i < Count; i++)
        {
            Span<byte> record = block.AsSpan(i * RecordBytes, RecordBytes);
            record[0] = (byte)i;
            total += record.Length;
        }

        return total;
    }

    /// <summary>
    /// A03: one <c>stackalloc</c> block, records sliced out of it. stackalloc has
    /// no free - the space stays taken until the method returns - so this is the
    /// cheapest possible bump, and the reason A03 keeps the record count small.
    /// </summary>
    [Benchmark]
    public long StackAlloc_SingleBlock()
    {
        Span<byte> block = stackalloc byte[RecordBytes * Count];
        long total = 0;
        for (int i = 0; i < Count; i++)
        {
            Span<byte> record = block.Slice(i * RecordBytes, RecordBytes);
            record[0] = (byte)i;
            total += record.Length;
        }

        return total;
    }
}

/// <summary>
/// A04: the zeroing axis of ADR-001 D5 (concept.md 5.2: the A series compares
/// AllocSpan, AllocSpanZeroed and ArrayPool on the same buffer). The arena hands
/// out raw memory
/// unless asked otherwise, the CLR zeroes every managed array, and ArrayPool
/// gives back whatever the previous renter left - the three are not
/// interchangeable, and this case prices the difference.
/// </summary>
public class AllocZeroingBench
{
    private Arena _arena = null!;

    /// <summary>Buffer size in bytes.</summary>
    [Params(32, 1024, 65536, 1048576)]
    public int SizeBytes { get; set; }

    [GlobalSetup]
    public void Setup() => _arena = new Arena((nuint)SizeBytes + 4096);

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    /// <summary>A04 baseline: the raw bump - no zeroing, only the offset moves (ADR-001 D5).</summary>
    [Benchmark(Baseline = true)]
    public long Arena_AllocSpan()
    {
        Span<byte> span = _arena.AllocSpan<byte>(SizeBytes);
        long probe = span[SizeBytes - 1]; // read the tail before the reset: after it the memory is gone (D9)
        long used = (long)_arena.UsedBytes;
        _arena.Reset();
        return probe + used;
    }

    /// <summary>A04: the explicit zeroed API (ADR-001 D5) - the same bump plus a full clear.</summary>
    [Benchmark]
    public long Arena_AllocSpanZeroed()
    {
        Span<byte> span = _arena.AllocSpanZeroed<byte>(SizeBytes);
        long probe = span[SizeBytes - 1];
        long used = (long)_arena.UsedBytes;
        _arena.Reset();
        return probe + used;
    }

    /// <summary>A04: a managed array - zeroed by the CLR, and visible to the GC.</summary>
    [Benchmark]
    public long ManagedArray_New()
    {
        byte[] buffer = new byte[SizeBytes];
        return buffer[SizeBytes - 1] + buffer.Length;
    }

    /// <summary>
    /// A04: a pooled array cleared by hand. ArrayPool does not zero what it hands
    /// back (the security note of concept.md 12.3), so a caller that needs zeros
    /// pays for the clear - and, above the largest bucket size, for a fresh
    /// allocation as well.
    /// </summary>
    [Benchmark]
    public long ArrayPool_RentThenClear()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(SizeBytes);
        buffer.AsSpan(0, SizeBytes).Clear();
        long probe = buffer[SizeBytes - 1] + buffer.Length;
        ArrayPool<byte>.Shared.Return(buffer);
        return probe;
    }
}


