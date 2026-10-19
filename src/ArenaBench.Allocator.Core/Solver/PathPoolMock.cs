using System.Collections.Concurrent;

namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// Mock of <c>NetGraph.Core.PathPool</c> (concept.md 5.2): two
/// <see cref="ConcurrentBag{T}"/> pools, one for <c>int[]</c> (path node ids) and
/// one for <c>float[]</c> (potentials / dual values), with
/// <c>Rent(minSize)</c> / <c>Return</c> semantics. A bag is thread-safe by
/// design, which is exactly why this is the parallel baseline in E04 and the
/// incumbent in E03: the arena has to beat a pool that already avoids the GC.
/// <para>
/// Rent hands back an array of at least <c>minSize</c>: a pooled one whose
/// length suffices, or a fresh one - and only then is there an allocation, which
/// <see cref="IntArrayAllocations"/> / <see cref="FloatArrayAllocations"/> count
/// so tests can assert that reuse really happens. A returned array keeps its
/// length (the pool never trims), the way netgraph's pool behaves.
/// </para>
/// </summary>
public sealed class PathPoolMock
{
    private readonly ConcurrentBag<int[]> _intPool = new();
    private readonly ConcurrentBag<float[]> _floatPool = new();
    private int _intAllocations;
    private int _floatAllocations;

    /// <summary>Number of <c>int[]</c> arrays the pool ever had to allocate.</summary>
    public int IntArrayAllocations => Volatile.Read(ref _intAllocations);

    /// <summary>Number of <c>float[]</c> arrays the pool ever had to allocate.</summary>
    public int FloatArrayAllocations => Volatile.Read(ref _floatAllocations);

    /// <summary>Arrays currently sitting in the <c>int[]</c> bag (approximate, bag semantics).</summary>
    public int IntPoolSize => _intPool.Count;

    /// <summary>Arrays currently sitting in the <c>float[]</c> bag (approximate, bag semantics).</summary>
    public int FloatPoolSize => _floatPool.Count;

    /// <summary>
    /// Rents an <c>int[]</c> of at least <paramref name="minSize"/> elements.
    /// A pooled array that is too small goes back to the bag (it can still serve
    /// a smaller request) and a fitting one is allocated instead.
    /// </summary>
    public int[] Rent(int minSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minSize);

        if (_intPool.TryTake(out int[]? pooled) && pooled.Length >= minSize)
        {
            return pooled;
        }

        if (pooled is not null)
        {
            _intPool.Add(pooled);
        }

        Interlocked.Increment(ref _intAllocations);
        return new int[minSize];
    }

    /// <summary>Rents a <c>float[]</c> of at least <paramref name="minSize"/> elements.</summary>
    public float[] RentFloat(int minSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minSize);

        if (_floatPool.TryTake(out float[]? pooled) && pooled.Length >= minSize)
        {
            return pooled;
        }

        if (pooled is not null)
        {
            _floatPool.Add(pooled);
        }

        Interlocked.Increment(ref _floatAllocations);
        return new float[minSize];
    }

    /// <summary>Returns an <c>int[]</c> to the pool.</summary>
    public void Return(int[] array)
    {
        ArgumentNullException.ThrowIfNull(array);
        _intPool.Add(array);
    }

    /// <summary>Returns a <c>float[]</c> to the pool.</summary>
    public void ReturnFloat(float[] array)
    {
        ArgumentNullException.ThrowIfNull(array);
        _floatPool.Add(array);
    }
}
