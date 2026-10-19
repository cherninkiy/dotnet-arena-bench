namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// CSR view over flat arrays (concept.md 5.2): <c>Offsets[nodeCount + 1]</c>,
/// <c>Targets/Capacities/Costs[edgeCount]</c>. Deliberately a <c>ref struct</c>:
/// the same type has to describe managed arrays (the baseline of E01/E02) and
/// arena memory (the candidate), and a <see cref="Span{T}"/> cannot be a field
/// of a regular struct.
/// </summary>
public readonly ref struct CsrGraphView
{
    /// <summary>
    /// Wraps existing CSR arrays. Checks the O(1) invariants only (lengths, the
    /// empty prefix, the edge count); the per-edge checks live in
    /// <see cref="Validate"/>, so building a view inside a benchmark iteration
    /// does not walk the whole graph.
    /// </summary>
    public CsrGraphView(
        ReadOnlySpan<int> offsets,
        ReadOnlySpan<int> targets,
        ReadOnlySpan<float> capacities,
        ReadOnlySpan<float> costs)
    {
        if (offsets.Length < 2)
        {
            throw new ArgumentException(
                "CSR needs at least one node: offsets holds nodeCount + 1 entries.", nameof(offsets));
        }

        if (targets.Length != capacities.Length || targets.Length != costs.Length)
        {
            throw new ArgumentException(
                $"CSR arrays must hold one entry per edge: targets {targets.Length}, capacities {capacities.Length}, costs {costs.Length}.",
                nameof(targets));
        }

        if (offsets[0] != 0 || offsets[^1] != targets.Length)
        {
            throw new ArgumentException("CSR offsets must start at 0 and end at the edge count.", nameof(offsets));
        }

        Offsets = offsets;
        Targets = targets;
        Capacities = capacities;
        Costs = costs;
    }

    /// <summary>Row pointers: one entry per node plus the edge count.</summary>
    public ReadOnlySpan<int> Offsets { get; }

    /// <summary>Edge targets, grouped by source node.</summary>
    public ReadOnlySpan<int> Targets { get; }

    /// <summary>Edge capacities, aligned with <see cref="Targets"/>.</summary>
    public ReadOnlySpan<float> Capacities { get; }

    /// <summary>Edge costs, aligned with <see cref="Targets"/>.</summary>
    public ReadOnlySpan<float> Costs { get; }

    /// <summary>Number of nodes: <c>offsets.Length - 1</c>.</summary>
    public int NodeCount => Offsets.Length - 1;

    /// <summary>Number of directed edges.</summary>
    public int EdgeCount => Targets.Length;

    /// <summary>Index of the first edge of a node's row.</summary>
    public int RowStart(int node)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(node, NodeCount);
        return Offsets[node];
    }

    /// <summary>Number of outgoing edges of a node.</summary>
    public int OutDegree(int node)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(node, NodeCount);
        return Offsets[node + 1] - Offsets[node];
    }

    /// <summary>
    /// Checks what the constructor skips: rows are non-decreasing and every
    /// target is a node. O(nodeCount + edgeCount) - for tests and for the
    /// solver's input contract, not for a benchmark iteration.
    /// </summary>
    /// <exception cref="InvalidOperationException">The layout is not a valid CSR.</exception>
    public void Validate()
    {
        int previous = 0;
        for (int node = 0; node <= NodeCount; node++)
        {
            int offset = Offsets[node];
            if (offset < previous || offset > EdgeCount)
            {
                throw new InvalidOperationException(
                    $"CSR offsets are not monotone at node {node}: {offset} after {previous}.");
            }

            previous = offset;
        }

        for (int edge = 0; edge < EdgeCount; edge++)
        {
            int target = Targets[edge];
            if (target < 0 || target >= NodeCount)
            {
                throw new InvalidOperationException(
                    $"CSR target {target} of edge {edge} is not a node (0..{NodeCount - 1}).");
            }
        }
    }
}
