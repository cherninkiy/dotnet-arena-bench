using System.Runtime.CompilerServices;

namespace ArenaBench.Allocator.Core.Solver;

/// <summary>
/// The CSR walks of E01 (concept.md 5.2). Every method takes a
/// <see cref="CsrGraphView"/>, so the identical code walks managed arrays and
/// arena memory - that is what makes the two measurements comparable.
/// </summary>
public static class CsrTraversal
{
    /// <summary>
    /// One sweep over the whole graph, row by row, accumulating target ids and
    /// costs - the pattern of a relaxation pass. Returns the sum, so the reads
    /// cannot be dropped as dead code.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Scan(CsrGraphView graph)
    {
        long sum = 0;
        for (int node = 0; node < graph.NodeCount; node++)
        {
            int end = graph.Offsets[node + 1];
            for (int edge = graph.Offsets[node]; edge < end; edge++)
            {
                sum += graph.Targets[edge] + (long)graph.Costs[edge];
            }
        }

        return sum;
    }

    /// <summary>
    /// Hop-count BFS from <paramref name="source"/> - the walk a Beam Search
    /// frontier does: a queue instead of a straight line, and a discovered map
    /// that makes the access pattern data-dependent.
    /// </summary>
    /// <param name="graph">Graph to walk.</param>
    /// <param name="source">Start node.</param>
    /// <param name="queue">
    /// Scratch queue, must hold every node at least once
    /// (<c>graph.NodeCount</c> entries).
    /// </param>
    /// <param name="discovered">
    /// Scratch marks, one entry per node (0 = not reached). Cleared by the call,
    /// because neither the arena nor the pools promise zeroed memory.
    /// </param>
    /// <returns>The number of reached nodes.</returns>
    public static int BreadthFirst(CsrGraphView graph, int source, Span<int> queue, Span<byte> discovered)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(source);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(source, graph.NodeCount);

        discovered.Clear();
        discovered[source] = 1;

        int head = 0;
        int tail = 0;
        queue[tail++] = source;

        while (head < tail)
        {
            int node = queue[head++];
            for (int edge = graph.Offsets[node]; edge < graph.Offsets[node + 1]; edge++)
            {
                int target = graph.Targets[edge];
                if (discovered[target] == 0)
                {
                    discovered[target] = 1;
                    queue[tail++] = target;
                }
            }
        }

        return tail;
    }
}
