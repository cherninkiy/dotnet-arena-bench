namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// The per-graph constants a branch-and-bound run needs (concept.md 5.2): the
/// per-hop lower bound and the widest row. Measuring them costs O(N) + O(E),
/// which is the same order as a shallow search, so E03/E05 compute them once per
/// case and pass them in: otherwise the solver's preparation - identical for
/// every scratch provider - would hide most of the allocator difference.
/// </summary>
/// <param name="CheapestEdge">
/// Cheapest edge of the graph, clamped to 0 when no edge is positive, which keeps
/// the pruning bound admissible.
/// </param>
/// <param name="ScratchLength">Floats one expanded node rents: the widest row.</param>
public readonly record struct BnBMetrics(float CheapestEdge, int ScratchLength);
