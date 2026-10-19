namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// Mini branch-and-bound over a CSR graph (concept.md 5.2): the cheapest walk
/// from a source to a sink with at most <see cref="MaxHops"/> edges. The search
/// is a depth-first branch-and-bound with an admissible bound (the cost so far
/// plus the cheapest edge of the graph times the hops left), so it prunes the
/// way the netgraph solver prunes path choices, and every expanded node rents
/// its row buffer through <see cref="IScratchProvider"/> - the arena rolls it
/// back, the pools hand the array back (E03).
/// <para>
/// Everything besides the per-node scratch is preallocated in the constructor:
/// the measurement has to isolate the allocator, not the framework around it.
/// </para>
/// <para>
/// Deterministic: the CSR adjacency order drives the expansion order, so all
/// three providers return the same cost for the same graph and budget. The
/// search is also <em>complete</em> while the budget lasts - pruning drops only
/// branches that cannot beat the incumbent - so its result equals a bounded
/// Bellman-Ford, which is what the tests compare against.
/// </para>
/// <para>
/// PRECONDITION: the CSR layout is valid. <see cref="CsrGraphView.Validate"/>
/// checks it; <see cref="Solve{TScratch}"/> only checks the O(1) things
/// (source and sink ranges) and does not walk the graph, so a benchmark
/// iteration measures the search, not the validation.
/// </para>
/// </summary>
public sealed class MiniBranchAndBound
{
    private readonly int[] _frameNode;
    private readonly int[] _frameEdge;
    private readonly float[] _frameCost;

    /// <summary>
    /// Creates the solver. <paramref name="nodeBudget"/> caps the number of
    /// expansions (the B&amp;B node budget of one solver tick),
    /// <paramref name="maxHops"/> caps the length of the walks it may build.
    /// </summary>
    /// <param name="nodeBudget">Expansions before the search stops with the best walk found so far.</param>
    /// <param name="maxHops">Maximum number of edges in a walk; also the DFS depth.</param>
    public MiniBranchAndBound(int nodeBudget = 1024, int maxHops = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeBudget, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHops, 1);

        NodeBudget = nodeBudget;
        MaxHops = maxHops;
        _frameNode = new int[maxHops + 1];
        _frameEdge = new int[maxHops + 1];
        _frameCost = new float[maxHops + 1];
    }

    /// <summary>Expansions allowed per solve.</summary>
    public int NodeBudget { get; }

    /// <summary>Maximum walk length in edges.</summary>
    public int MaxHops { get; }

    /// <summary>Expansions the last <see cref="Solve{TScratch}"/> performed; the budget cap shows up here.</summary>
    public int Expansions { get; private set; }

    /// <summary>Floats the last <see cref="Solve{TScratch}"/> rented per node (the widest row).</summary>
    public int ScratchLength { get; private set; }

    /// <summary>Floats one node of <paramref name="graph"/> rents: the widest row.</summary>
    public static int ScratchLengthFor(CsrGraphView graph)
    {
        int widest = 0;
        for (int node = 0; node < graph.NodeCount; node++)
        {
            int degree = graph.Offsets[node + 1] - graph.Offsets[node];
            if (degree > widest)
            {
                widest = degree;
            }
        }

        return Math.Max(widest, 1);
    }

    /// <summary>
    /// Measures the per-graph constants once, so that a benchmark (or a solver
    /// tick) does not pay for them inside every search. The layout is validated
    /// here as well: the call already walks the graph, and a malformed CSR should
    /// fail once, at preparation, instead of quietly producing a wrong bound.
    /// </summary>
    /// <param name="graph">CSR graph to measure.</param>
    /// <exception cref="InvalidOperationException">The layout is not a valid CSR.</exception>
    public static BnBMetrics Measure(CsrGraphView graph)
    {
        graph.Validate();
        return new BnBMetrics(CheapestEdge(graph), ScratchLengthFor(graph));
    }

    /// <summary>
    /// Cheapest source-to-sink walk with at most <see cref="MaxHops"/> edges, or
    /// <see cref="float.PositiveInfinity"/> when the sink is unreachable within
    /// that many hops. A walk may revisit nodes: the cost bound, not a visited
    /// set, is what keeps the search finite.
    /// </summary>
    /// <typeparam name="TScratch">Scratch provider under test (a struct, so the call is devirtualized).</typeparam>
    /// <param name="graph">Valid CSR graph (see the class remarks).</param>
    /// <param name="metrics">Per-graph constants from <see cref="Measure"/>.</param>
    /// <param name="source">Start node.</param>
    /// <param name="sink">Target node.</param>
    /// <param name="scratch">Provider the per-node row buffer comes from.</param>
    public float Solve<TScratch>(
        CsrGraphView graph,
        BnBMetrics metrics,
        int source,
        int sink,
        ref TScratch scratch)
        where TScratch : struct, IScratchProvider
    {
        ArgumentOutOfRangeException.ThrowIfNegative(source);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(source, graph.NodeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(sink);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sink, graph.NodeCount);

        Expansions = 0;
        ScratchLength = metrics.ScratchLength;

        if (source == sink)
        {
            return 0f;
        }

        // Both values come from Measure: the lower bound per remaining hop (clamped
        // to 0, which keeps it admissible for negative costs or an edgeless graph)
        // and the row width. They cost O(N)+O(E), so they are not part of the search
        // the benchmark is pricing.
        float cheapestEdge = metrics.CheapestEdge;
        float best = float.PositiveInfinity;

        int depth = 0;
        _frameNode[0] = source;
        _frameEdge[0] = graph.Offsets[source];
        _frameCost[0] = 0f;

        while (depth >= 0 && Expansions < NodeBudget)
        {
            int node = _frameNode[depth];
            int cursor = _frameEdge[depth];
            if (cursor >= graph.Offsets[node + 1])
            {
                depth--;
                continue;
            }

            _frameEdge[depth] = cursor + 1;
            int child = graph.Targets[cursor];
            float childCost = _frameCost[depth] + graph.Costs[cursor];

            if (child == sink)
            {
                if (childCost < best)
                {
                    best = childCost;
                }

                continue;
            }

            int childDepth = depth + 1;
            if (childDepth >= MaxHops)
            {
                continue;
            }

            int hopsLeft = MaxHops - childDepth;
            if (childCost + (cheapestEdge * hopsLeft) >= best)
            {
                continue;
            }

            Expansions++;

            // One expansion = one search node: it estimates every outgoing edge
            // of the child into its row buffer, then gives the buffer back. The
            // arena pays an offset rollback, the pools pay a bag round trip.
            int rowStart = graph.Offsets[child];
            int degree = graph.Offsets[child + 1] - rowStart;
            Span<float> estimates = scratch.Rent(ScratchLength).Slice(0, degree);
            estimates.Clear();

            float cheapestContinuation = float.PositiveInfinity;
            for (int edge = 0; edge < degree; edge++)
            {
                float estimate = childCost + graph.Costs[rowStart + edge];
                estimates[edge] = estimate;
                if (estimate < cheapestContinuation)
                {
                    cheapestContinuation = estimate;
                }
            }

            scratch.Release();

            // The same admissible bound, one hop further: any completion of this
            // branch costs at least the cheapest continuation.
            if (cheapestContinuation + (cheapestEdge * (hopsLeft - 1)) >= best)
            {
                continue;
            }

            depth = childDepth;
            _frameNode[depth] = child;
            _frameEdge[depth] = rowStart;
            _frameCost[depth] = childCost;
        }

        return best;
    }

    /// <summary>
    /// Cheapest edge of the graph - the per-hop term of the pruning bound.
    /// Clamped to 0 when no edge is positive (negative costs, empty graph),
    /// which keeps the bound a lower bound: the only thing pruning may be.
    /// </summary>
    private static float CheapestEdge(CsrGraphView graph)
    {
        float cheapest = float.PositiveInfinity;
        for (int edge = 0; edge < graph.EdgeCount; edge++)
        {
            float cost = graph.Costs[edge];
            if (cost < cheapest)
            {
                cheapest = cost;
            }
        }

        return cheapest is > 0f ? cheapest : 0f;
    }
}
