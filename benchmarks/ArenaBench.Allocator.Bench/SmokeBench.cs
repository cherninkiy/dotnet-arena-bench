using System.Buffers;
using ArenaBench.Core;
using BenchmarkDotNet.Attributes;

namespace ArenaBench.Allocator.Bench;

/// <summary>
/// Day-0 smoke benchmark: verifies the BenchmarkDotNet setup and the
/// arena skeleton (ADR-001). Replaced by the A/E/I/M-series on Days 3–4
/// (concept.md, День 3–4).
///
/// The arena lives across iterations ([GlobalSetup]/[GlobalCleanup]) so that
/// the comparison with the pooled ArrayPool variant is apples-to-apples
/// (both reuse their storage); the cost of creating and freeing the block is
/// measured separately by <see cref="Arena_CreateDispose"/>.
/// </summary>
public class SmokeBench
{
    private const int SizeBytes = 1 << 20;
    private Arena _arena = null!;

    [GlobalSetup]
    public void Setup() => _arena = new Arena((nuint)SizeBytes);

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    [Benchmark(Baseline = true)]
    public int ArrayPool_RentReturn()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(SizeBytes);
        int length = buffer.Length; // read before Return: the array goes back to the pool
        ArrayPool<byte>.Shared.Return(buffer);
        return length;
    }

    [Benchmark]
    public long Arena_AllocReset()
    {
        Span<byte> span = _arena.AllocSpan<byte>(SizeBytes);
        span[0] = 1;
        long used = (long)_arena.UsedBytes;
        _arena.Reset();
        return used;
    }

    [Benchmark]
    public long Arena_MarkRelease()
    {
        ArenaMark mark = _arena.Mark();
        Span<byte> span = _arena.AllocSpan<byte>(SizeBytes);
        span[0] = 1;
        _arena.Release(mark);
        return (long)_arena.UsedBytes;
    }

    [Benchmark]
    public long Arena_CreateDispose()
    {
        using var arena = new Arena((nuint)SizeBytes);
        Span<byte> span = arena.AllocSpan<byte>(SizeBytes);
        span[0] = 1;
        return (long)arena.UsedBytes;
    }
}
