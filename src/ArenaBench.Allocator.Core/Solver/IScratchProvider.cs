namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// The per-search-node scratch rental under test (concept.md 5.2, E03): the
/// arena rolls back with <see cref="Arena.Mark"/>/<see cref="Arena.Release"/>,
/// the path pool and the array pool hand their buffer back to a bag.
/// <para>
/// Implemented by structs on purpose:
/// <see cref="MiniBranchAndBound.Solve{TScratch}"/> is generic over the
/// provider, so the constrained call is devirtualized and the interface adds no
/// dispatch to the measured loop - it exists to keep the variants
/// interchangeable.
/// </para>
/// <para>
/// Rent/Release pair up LIFO: Release gives back exactly what the matching Rent
/// took. Neither the arena (ADR-001 D5) nor the pools promise zeroed memory, so
/// the caller clears what it needs.
/// </para>
/// </summary>
public interface IScratchProvider
{
    /// <summary>Rents at least <paramref name="size"/> floats for one search node.</summary>
    Span<float> Rent(int size);

    /// <summary>Gives back the buffer taken by the matching <see cref="Rent"/>.</summary>
    void Release();
}
