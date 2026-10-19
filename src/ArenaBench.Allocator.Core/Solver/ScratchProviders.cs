using System.Buffers;

using ArenaBench.Core;

namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// E03's scratch provider over the arena (ADR-001 D4): <see cref="Rent"/> takes
/// a mark and bumps the offset, <see cref="Release"/> rolls it back. O(1), and
/// nothing else to touch - no array header, no bag, no length bookkeeping.
/// </summary>
public struct ArenaScratchProvider : IScratchProvider
{
    private readonly Arena _arena;
    private ArenaMark _mark;

    /// <summary>Creates the provider over <paramref name="arena"/>.</summary>
    public ArenaScratchProvider(Arena arena) => _arena = arena;

    /// <inheritdoc />
    public Span<float> Rent(int size)
    {
        _mark = _arena.Mark();
        return _arena.AllocSpan<float>(size);
    }

    /// <inheritdoc />
    public void Release() => _arena.Release(_mark);
}

/// <summary>
/// E03's scratch provider over <see cref="PathPoolMock"/> - the incumbent from
/// netgraph-optimizer: thread-safe by construction, but every Rent and Return
/// touches a concurrent collection.
/// </summary>
public struct PathPoolScratchProvider : IScratchProvider
{
    private readonly PathPoolMock _pool;
    private float[]? _rented;

    /// <summary>Creates the provider over <paramref name="pool"/>.</summary>
    public PathPoolScratchProvider(PathPoolMock pool) => _pool = pool;

    /// <inheritdoc />
    public Span<float> Rent(int size)
    {
        _rented = _pool.RentFloat(size);
        return _rented.AsSpan();
    }

    /// <inheritdoc />
    public void Release()
    {
        if (_rented is not null)
        {
            _pool.ReturnFloat(_rented);
            _rented = null;
        }
    }
}

/// <summary>E03's scratch provider over <see cref="ArrayPool{T}"/> - the BCL answer to the same problem.</summary>
public struct ArrayPoolScratchProvider : IScratchProvider
{
    private readonly ArrayPool<float> _pool;
    private float[]? _rented;

    /// <summary>Creates the provider over <paramref name="pool"/> (usually <c>ArrayPool&lt;float&gt;.Shared</c>).</summary>
    public ArrayPoolScratchProvider(ArrayPool<float> pool) => _pool = pool;

    /// <inheritdoc />
    public Span<float> Rent(int size)
    {
        _rented = _pool.Rent(size);
        return _rented.AsSpan();
    }

    /// <inheritdoc />
    public void Release()
    {
        if (_rented is not null)
        {
            _pool.Return(_rented);
            _rented = null;
        }
    }
}
