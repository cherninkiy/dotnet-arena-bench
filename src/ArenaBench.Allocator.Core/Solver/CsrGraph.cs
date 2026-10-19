using ArenaBench.Core;

namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// Micro-CSR graph generated the way netgraph-optimizer's
/// <c>GraphGenerator.CreateRandom</c> does it (concept.md 5.2): a ring plus a
/// fixed number of extra edges per node, drawn from a seeded generator, so a
/// run is reproducible (<c>new Random(42)</c>, concept.md 7.4).
/// <para>
/// The graph owns managed arrays. <see cref="View"/> describes them and
/// <see cref="CopyTo(Arena)"/> copies the whole layout into arena memory - that
/// pair is the axis E01/E02 measure.
/// </para>
/// </summary>
public sealed class CsrGraph
{
    /// <summary>Seed of the generated graphs - fixed for reproducible runs (concept.md 7.4).</summary>
    public const int DefaultSeed = 42;

    private const float MinCost = 0.5f;
    private const float CostRange = 3.5f;
    private const int MaxCapacity = 10;

    private readonly int[] _offsets;
    private readonly int[] _targets;
    private readonly float[] _capacities;
    private readonly float[] _costs;

    private CsrGraph(int[] offsets, int[] targets, float[] capacities, float[] costs)
    {
        _offsets = offsets;
        _targets = targets;
        _capacities = capacities;
        _costs = costs;
    }

    /// <summary>Number of nodes.</summary>
    public int NodeCount => _offsets.Length - 1;

    /// <summary>Number of directed edges.</summary>
    public int EdgeCount => _targets.Length;

    /// <summary>Row pointers: one entry per node plus the edge count.</summary>
    public ReadOnlySpan<int> Offsets => _offsets;

    /// <summary>Edge targets, grouped by source node.</summary>
    public ReadOnlySpan<int> Targets => _targets;

    /// <summary>Edge capacities, aligned with <see cref="Targets"/>.</summary>
    public ReadOnlySpan<float> Capacities => _capacities;

    /// <summary>Edge costs, aligned with <see cref="Targets"/>.</summary>
    public ReadOnlySpan<float> Costs => _costs;

    /// <summary>The graph as a view over its managed arrays.</summary>
    public CsrGraphView View => new(_offsets, _targets, _capacities, _costs);

    /// <summary>
    /// Ring plus extra edges, netgraph-style: node <c>i</c> always reaches
    /// <c>i + 1</c> (wrapping), then <paramref name="extraEdgesPerNode"/> random
    /// targets follow, so the row layout is deterministic but the choice is
    /// not. Capacities are 1..10, costs 0.5..4.0 - drawn from the same
    /// <see cref="Random"/> sequence, which makes every array a function of the
    /// seed alone.
    /// </summary>
    /// <param name="nodeCount">Nodes in the ring; at least 2.</param>
    /// <param name="extraEdgesPerNode">Additional random edges per node.</param>
    /// <param name="seed">Generator seed; the default is the PoC seed.</param>
    public static CsrGraph CreateRandom(int nodeCount, int extraEdgesPerNode = 2, int seed = DefaultSeed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(extraEdgesPerNode);

        var random = new Random(seed);
        int edgeCount = nodeCount * (1 + extraEdgesPerNode);
        int[] offsets = new int[nodeCount + 1];
        int[] targets = new int[edgeCount];
        float[] capacities = new float[edgeCount];
        float[] costs = new float[edgeCount];

        int edge = 0;
        for (int node = 0; node < nodeCount; node++)
        {
            offsets[node] = edge;

            // The ring edge keeps the graph connected; the extra edges are what
            // the solver has to weigh against it.
            targets[edge] = (node + 1) % nodeCount;
            capacities[edge] = random.Next(1, MaxCapacity + 1);
            costs[edge] = NextCost(random);
            edge++;

            for (int extra = 0; extra < extraEdgesPerNode; extra++)
            {
                targets[edge] = random.Next(nodeCount);
                capacities[edge] = random.Next(1, MaxCapacity + 1);
                costs[edge] = NextCost(random);
                edge++;
            }
        }

        offsets[nodeCount] = edge;
        return new CsrGraph(offsets, targets, capacities, costs);
    }

    /// <summary>
    /// Copies the CSR layout into arena memory (the "build the graph in the
    /// arena" side of E02) and returns a view over the copy. The arena keeps the
    /// memory (ADR-001 D5: no zeroing), which is why the copy writes every slot.
    /// </summary>
    public CsrGraphView CopyTo(Arena arena)
    {
        ArgumentNullException.ThrowIfNull(arena);

        Span<int> offsets = arena.AllocSpan<int>(_offsets.Length);
        Span<int> targets = arena.AllocSpan<int>(_targets.Length);
        Span<float> capacities = arena.AllocSpan<float>(_capacities.Length);
        Span<float> costs = arena.AllocSpan<float>(_costs.Length);

        CopyTo(offsets, targets, capacities, costs);
        return new CsrGraphView(offsets, targets, capacities, costs);
    }

    /// <summary>
    /// Fills caller-provided CSR arrays. Both E02 variants (arena spans and
    /// managed arrays) go through this method, so the only difference left
    /// between them is where the memory comes from.
    /// </summary>
    public void CopyTo(Span<int> offsets, Span<int> targets, Span<float> capacities, Span<float> costs)
    {
        _offsets.CopyTo(offsets);
        _targets.CopyTo(targets);
        _capacities.CopyTo(capacities);
        _costs.CopyTo(costs);
    }

    private static float NextCost(Random random) => MinCost + ((float)random.NextDouble() * CostRange);
}
