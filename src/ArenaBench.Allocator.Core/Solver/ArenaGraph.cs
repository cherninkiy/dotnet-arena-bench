using ArenaBench.Core;

namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// A <see cref="CsrGraph"/> copied into arena memory, plus the views over the
/// copy (E01/E02: "the graph lives in the arena"). The layout is copied once,
/// the four array addresses are remembered, and <see cref="View"/> rebuilds the
/// CSR view over exactly those addresses without bumping the arena again -
/// otherwise every iteration would measure a copy instead of a traversal.
/// <para>
/// The owner is the arena: disposing frees the block and every view obtained
/// from it dies with it (ADR-001 D9).
/// </para>
/// </summary>
public sealed unsafe class ArenaGraph : IDisposable
{
    /// <summary>
    /// Four allocations, each of them aligned to 16 bytes (ADR-001 D3), so the
    /// worst-case padding between them is 3 x 16 bytes plus the trailing one.
    /// </summary>
    private const int PaddingSlack = 4 * 16;

    private readonly Arena _arena;
    private readonly IntPtr _offsets;
    private readonly IntPtr _targets;
    private readonly IntPtr _capacities;
    private readonly IntPtr _costs;
    private readonly int _offsetsLength;
    private readonly int _targetsLength;
    private readonly int _capacitiesLength;
    private readonly int _costsLength;

    /// <summary>Copies <paramref name="graph"/> into a fresh arena block.</summary>
    public ArenaGraph(CsrGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        _offsetsLength = graph.Offsets.Length;
        _targetsLength = graph.Targets.Length;
        _capacitiesLength = graph.Capacities.Length;
        _costsLength = graph.Costs.Length;

        _arena = new Arena((nuint)(RequiredBytes(graph) + PaddingSlack), defaultAlign: 16);
        _offsets = _arena.AllocPtr<int>(_offsetsLength);
        _targets = _arena.AllocPtr<int>(_targetsLength);
        _capacities = _arena.AllocPtr<float>(_capacitiesLength);
        _costs = _arena.AllocPtr<float>(_costsLength);

        graph.CopyTo(
            new Span<int>((void*)_offsets, _offsetsLength),
            new Span<int>((void*)_targets, _targetsLength),
            new Span<float>((void*)_capacities, _capacitiesLength),
            new Span<float>((void*)_costs, _costsLength));
    }

    /// <summary>The arena that owns the copied layout.</summary>
    public Arena Arena => _arena;

    /// <summary>CSR view over the arena copy; valid until the arena is disposed.</summary>
    public CsrGraphView View => new(
        new ReadOnlySpan<int>((void*)_offsets, _offsetsLength),
        new ReadOnlySpan<int>((void*)_targets, _targetsLength),
        new ReadOnlySpan<float>((void*)_capacities, _capacitiesLength),
        new ReadOnlySpan<float>((void*)_costs, _costsLength));

    /// <summary>Bytes the four arrays need, without alignment padding.</summary>
    public static int RequiredBytes(CsrGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return (graph.Offsets.Length * sizeof(int))
            + (graph.Targets.Length * sizeof(int))
            + (graph.Capacities.Length * sizeof(float))
            + (graph.Costs.Length * sizeof(float));
    }

    /// <inheritdoc />
    public void Dispose() => _arena.Dispose();
}
