using System.Buffers;

using ArenaBench.Allocator.Core.Solver;
using ArenaBench.Core;

namespace ArenaBench.Allocator.Tests;

/// <summary>
/// The mini B&amp;B has to be a real search: E03 compares three scratch
/// allocators on it, so its result must not depend on which one is plugged in,
/// and the pruning must not cost optimality while the node budget lasts. The
/// reference is a hop-limited Bellman-Ford - the textbook answer for the cheapest
/// walk with at most N edges, revisits allowed, which is exactly what the search
/// claims to find.
/// </summary>
public class MiniBranchAndBoundTests
{
    private const int MaxHops = 6;

    [Fact]
    public void Solve_MatchesBoundedBellmanFordForEveryProvider()
    {
        const int nodes = 64;
        const int source = 0;
        const int sink = 32;
        CsrGraph graph = CsrGraph.CreateRandom(nodes);
        float expected = ReferenceBoundedBellmanFord(graph, source, sink, MaxHops);

        var solver = new MiniBranchAndBound(nodeBudget: 4096, maxHops: MaxHops);
        using var arena = new Arena(ScratchBytes(graph));
        var pool = new PathPoolMock();

        var arenaScratch = new ArenaScratchProvider(arena);
        float fromArena = solver.Solve(graph.View, source, sink, ref arenaScratch);
        int arenaExpansions = solver.Expansions;

        var poolScratch = new PathPoolScratchProvider(pool);
        float fromPool = solver.Solve(graph.View, source, sink, ref poolScratch);

        var arrayScratch = new ArrayPoolScratchProvider(ArrayPool<float>.Shared);
        float fromArrayPool = solver.Solve(graph.View, source, sink, ref arrayScratch);

        Assert.Equal(expected, fromArena, 3);
        Assert.Equal(fromArena, fromPool);       // the allocator must not change the answer
        Assert.Equal(fromArena, fromArrayPool);
        Assert.True(arenaExpansions < 4096, "the budget must not bind on this graph");
        Assert.Equal(arenaExpansions, solver.Expansions); // and the expansion count is deterministic
    }

    [Fact]
    public void Solve_LeavesTheArenaRolledBack()
    {
        CsrGraph graph = CsrGraph.CreateRandom(64);
        using var arena = new Arena(ScratchBytes(graph));
        var solver = new MiniBranchAndBound(nodeBudget: 512, maxHops: MaxHops);

        var scratch = new ArenaScratchProvider(arena);
        _ = solver.Solve(graph.View, 0, 32, ref scratch);

        // Every rent was released: the arena sits exactly where it started, which
        // is the property E03 compares against the pool round trips (ADR-001 D4).
        Assert.Equal(0u, (uint)arena.UsedBytes);
        Assert.True(solver.Expansions > 0);
    }

    [Fact]
    public void Solve_ReturnsTheRentedArrayToThePool()
    {
        CsrGraph graph = CsrGraph.CreateRandom(64);
        var pool = new PathPoolMock();
        var solver = new MiniBranchAndBound(nodeBudget: 512, maxHops: MaxHops);

        var scratch = new PathPoolScratchProvider(pool);
        _ = solver.Solve(graph.View, 0, 32, ref scratch);

        Assert.Equal(1, pool.FloatPoolSize);
        Assert.Equal(1, pool.FloatArrayAllocations); // one buffer, reused for every node
    }

    [Fact]
    public void Solve_RespectsTheNodeBudget()
    {
        CsrGraph graph = CsrGraph.CreateRandom(64);
        using var arena = new Arena(ScratchBytes(graph));
        var solver = new MiniBranchAndBound(nodeBudget: 16, maxHops: MaxHops);

        var scratch = new ArenaScratchProvider(arena);
        _ = solver.Solve(graph.View, 0, 32, ref scratch);

        Assert.Equal(16, solver.Expansions);
    }

    [Fact]
    public void Solve_IgnoresWalksLongerThanMaxHops()
    {
        // 0 -> 1 -> 2 -> 3 -> 4: four edges to the sink.
        int[] offsets = [0, 1, 2, 3, 4, 4];
        int[] targets = [1, 2, 3, 4];
        float[] capacities = [1f, 1f, 1f, 1f];
        float[] costs = [1f, 1f, 1f, 1f];
        var view = new CsrGraphView(offsets, targets, capacities, costs);

        Assert.Equal(float.PositiveInfinity, Solve(view, maxHops: 2));
        Assert.Equal(float.PositiveInfinity, Solve(view, maxHops: 3));
        Assert.Equal(4f, Solve(view, maxHops: 4));
    }

    [Fact]
    public void Solve_HandlesNegativeCosts()
    {
        // 0 -> 2 costs 10, 0 -> 1 -> 2 costs 2 - 5: the cheaper walk needs the
        // negative edge, so the pruning bound has to stay admissible with it.
        int[] offsets = [0, 2, 3, 4, 4];
        int[] targets = [1, 2, 2, 3];
        float[] capacities = [1f, 1f, 1f, 1f];
        float[] costs = [2f, 10f, -5f, 1f];
        var view = new CsrGraphView(offsets, targets, capacities, costs);

        Assert.Equal(-2f, Solve(view, maxHops: 3));
    }

    [Fact]
    public void Solve_SourceEqualsSinkIsZero()
    {
        CsrGraph graph = CsrGraph.CreateRandom(16);
        using var arena = new Arena(ScratchBytes(graph));
        var solver = new MiniBranchAndBound(nodeBudget: 64, maxHops: MaxHops);
        var scratch = new ArenaScratchProvider(arena);

        Assert.Equal(0f, solver.Solve(graph.View, 3, 3, ref scratch));
        Assert.Equal(0, solver.Expansions);
    }

    [Fact]
    public void Solve_RejectsNodesOutsideTheGraph()
    {
        CsrGraph graph = CsrGraph.CreateRandom(16);
        using var arena = new Arena(ScratchBytes(graph));
        var solver = new MiniBranchAndBound(nodeBudget: 64, maxHops: MaxHops);
        var scratch = new ArenaScratchProvider(arena);

        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(graph.View, 16, 0, ref scratch));
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(graph.View, 0, -1, ref scratch));
    }

    [Fact]
    public void ScratchLengthFor_ReturnsTheWidestRow()
    {
        CsrGraph graph = CsrGraph.CreateRandom(32, extraEdgesPerNode: 3);

        Assert.Equal(4, MiniBranchAndBound.ScratchLengthFor(graph.View));
    }

    /// <summary>Solves source -> last node of <paramref name="view"/> with a one-off arena.</summary>
    private static float Solve(CsrGraphView view, int maxHops)
    {
        var solver = new MiniBranchAndBound(nodeBudget: 1024, maxHops: maxHops);
        using var arena = new Arena((nuint)((MiniBranchAndBound.ScratchLengthFor(view) * sizeof(float)) + 64));
        var scratch = new ArenaScratchProvider(arena);
        return solver.Solve(view, 0, view.NodeCount - 1, ref scratch);
    }

    private static nuint ScratchBytes(CsrGraph graph) =>
        (nuint)((MiniBranchAndBound.ScratchLengthFor(graph.View) * sizeof(float)) + 64);

    /// <summary>
    /// Hop-limited Bellman-Ford: the cheapest walk of at most
    /// <paramref name="maxHops"/> edges, revisits allowed - the reference the
    /// branch-and-bound has to agree with.
    /// </summary>
    private static float ReferenceBoundedBellmanFord(CsrGraph graph, int source, int sink, int maxHops)
    {
        int nodes = graph.NodeCount;
        float[] best = new float[nodes];
        Array.Fill(best, float.PositiveInfinity);
        best[source] = 0f;

        for (int hop = 0; hop < maxHops; hop++)
        {
            float[] next = (float[])best.Clone();
            for (int node = 0; node < nodes; node++)
            {
                if (float.IsPositiveInfinity(best[node]))
                {
                    continue;
                }

                for (int edge = graph.Offsets[node]; edge < graph.Offsets[node + 1]; edge++)
                {
                    int target = graph.Targets[edge];
                    float candidate = best[node] + graph.Costs[edge];
                    if (candidate < next[target])
                    {
                        next[target] = candidate;
                    }
                }
            }

            best = next;
        }

        return best[sink];
    }
}
