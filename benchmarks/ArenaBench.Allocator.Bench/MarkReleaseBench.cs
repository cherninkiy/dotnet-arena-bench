using System.Buffers;

using ArenaBench.Core;

using BenchmarkDotNet.Attributes;

namespace ArenaBench.Allocator.Bench;

/// <summary>
/// A05: the pair <see cref="Arena.Mark"/>/<see cref="Arena.Release"/> on its own,
/// next to the same pair wrapped around one small allocation and next to
/// <c>ArrayPool</c>. E03 ends with the arena behind <c>ArrayPool</c> in a
/// rent/release-per-node loop; this case shows where that difference comes from
/// and how much of it the two calls themselves are - the
/// <see cref="ArenaMark"/> that gets built and the checks in
/// <see cref="Arena.Release"/> (arena identity, generation, offset), not the bump.
/// </summary>
/// <remarks>
/// The measured calls are exactly what a node-scoped scratch rental costs in the
/// solver's shape (concept.md 5.2): take a mark, allocate, give the space back.
/// Nothing is reset per iteration - <see cref="Arena.Release"/> returns the offset
/// to the mark, so the arena stays at its first bytes and does not grow across
/// iterations, and the same block is reused for the whole run.
/// </remarks>
public class MarkReleaseBench
{
    private const int SizeBytes = 32;

    private Arena _arena = null!;

    /// <summary>Both sub-cases use the same 32-byte buffer, so the three columns differ only in mechanism.</summary>
    [Params(32, 1024)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() => _arena = new Arena((nuint)Size + 64);

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    /// <summary>A05 baseline: the pooled managed array, the variant that wins E03.</summary>
    [Benchmark(Baseline = true)]
    public int ArrayPool_RentReturn()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Size);
        buffer[0] = 1;
        int length = buffer.Length; // read before Return: the array goes back to the pool
        ArrayPool<byte>.Shared.Return(buffer);
        return length;
    }

    /// <summary>A05: mark, one bump, release - the E03 shape without the solver around it.</summary>
    [Benchmark]
    public long Arena_MarkAllocRelease()
    {
        ArenaMark mark = _arena.Mark();
        Span<byte> span = _arena.AllocSpan<byte>(Size);
        span[0] = 1;
        long used = (long)_arena.UsedBytes;
        _arena.Release(mark);
        return used;
    }

    /// <summary>
    /// A05: the two calls alone, nothing allocated in between. The delta to the
    /// case above is the bump itself; the rest is <see cref="Arena.Mark"/>,
    /// <see cref="Arena.Release"/> and its three checks.
    /// </summary>
    [Benchmark]
    public long Arena_MarkRelease()
    {
        ArenaMark mark = _arena.Mark();
        _arena.Release(mark);
        return (long)_arena.UsedBytes;
    }
}
