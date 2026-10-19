using ArenaBench.Allocator.Core.Solver;
using ArenaBench.Core;

namespace ArenaBench.Allocator.Tests;

/// <summary>
/// The micro-CSR of PoC-2: the generator (deterministic, ring plus extra edges),
/// the view invariants and the two ways of holding the layout - managed arrays
/// and an arena block. E01/E02 compare those two, so they have to describe the
/// very same graph.
/// </summary>
public class CsrGraphTests
{
    [Fact]
    public void CreateRandom_IsDeterministicForOneSeed()
    {
        CsrGraph first = CsrGraph.CreateRandom(64);
        CsrGraph second = CsrGraph.CreateRandom(64);

        Assert.Equal(first.Offsets.ToArray(), second.Offsets.ToArray());
        Assert.Equal(first.Targets.ToArray(), second.Targets.ToArray());
        Assert.Equal(first.Capacities.ToArray(), second.Capacities.ToArray());
        Assert.Equal(first.Costs.ToArray(), second.Costs.ToArray());
    }

    [Fact]
    public void CreateRandom_DiffersBetweenSeeds()
    {
        Assert.NotEqual(
            CsrGraph.CreateRandom(64, seed: 1).Targets.ToArray(),
            CsrGraph.CreateRandom(64, seed: 2).Targets.ToArray());
    }

    [Fact]
    public void CreateRandom_BuildsARingPlusExtraEdges()
    {
        const int nodes = 32;
        const int extra = 2;
        CsrGraph graph = CsrGraph.CreateRandom(nodes, extra);

        Assert.Equal(nodes, graph.NodeCount);
        Assert.Equal(nodes * (1 + extra), graph.EdgeCount);
        graph.View.Validate();

        for (int node = 0; node < nodes; node++)
        {
            Assert.Equal(extra + 1, graph.View.OutDegree(node));
            Assert.Equal(node * (extra + 1), graph.View.RowStart(node));

            // The ring edge keeps the graph connected, whatever the seed draws.
            Assert.Equal((node + 1) % nodes, graph.Targets[graph.Offsets[node]]);
        }

        Assert.All(graph.Costs.ToArray(), cost => Assert.InRange(cost, 0.5f, 4f));
        Assert.All(graph.Capacities.ToArray(), capacity => Assert.InRange(capacity, 1f, 10f));
    }

    [Fact]
    public void CreateRandom_RejectsTooFewNodes() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CsrGraph.CreateRandom(1));

    [Fact]
    public void View_RejectsMismatchedArrayLengths()
    {
        int[] offsets = [0, 1];
        int[] targets = [0];
        float[] capacities = [1f];
        float[] costs = [1f, 2f];

        Assert.Throws<ArgumentException>(() => new CsrGraphView(offsets, targets, capacities, costs));
    }

    [Fact]
    public void View_RejectsOffsetsThatDoNotCoverTheEdges()
    {
        int[] offsets = [0, 5];
        int[] targets = [0];
        float[] capacities = [1f];
        float[] costs = [1f];

        Assert.Throws<ArgumentException>(() => new CsrGraphView(offsets, targets, capacities, costs));
    }

    [Fact]
    public void Validate_RejectsANonNodeTarget()
    {
        int[] offsets = [0, 1];
        int[] targets = [7]; // there is no node 7
        float[] capacities = [1f];
        float[] costs = [1f];

        // The view is built inside the lambda: a ref struct cannot be captured.
        Assert.Throws<InvalidOperationException>(() =>
        {
            var view = new CsrGraphView(offsets, targets, capacities, costs);
            view.Validate();
        });
    }

    [Fact]
    public void CopyTo_SpanFillsTheProvidedArrays()
    {
        CsrGraph graph = CsrGraph.CreateRandom(16);
        int[] offsets = new int[graph.Offsets.Length];
        int[] targets = new int[graph.Targets.Length];
        float[] capacities = new float[graph.Capacities.Length];
        float[] costs = new float[graph.Costs.Length];

        graph.CopyTo(offsets, targets, capacities, costs);

        Assert.Equal(graph.Offsets.ToArray(), offsets);
        Assert.Equal(graph.Targets.ToArray(), targets);
        Assert.Equal(graph.Capacities.ToArray(), capacities);
        Assert.Equal(graph.Costs.ToArray(), costs);
    }

    [Fact]
    public void ArenaGraph_DescribesTheVerySameGraph()
    {
        CsrGraph graph = CsrGraph.CreateRandom(32);
        using var arenaGraph = new ArenaGraph(graph);

        CsrGraphView view = arenaGraph.View;
        Assert.Equal(graph.Offsets.ToArray(), view.Offsets.ToArray());
        Assert.Equal(graph.Targets.ToArray(), view.Targets.ToArray());
        Assert.Equal(graph.Capacities.ToArray(), view.Capacities.ToArray());
        Assert.Equal(graph.Costs.ToArray(), view.Costs.ToArray());

        Assert.Equal(CsrTraversal.Scan(graph.View), CsrTraversal.Scan(view));
    }

    [Fact]
    public void ArenaGraph_KeepsTheLayoutWhenTheViewIsRebuilt()
    {
        CsrGraph graph = CsrGraph.CreateRandom(32);
        using var arenaGraph = new ArenaGraph(graph);

        // Rebuilding the view must not bump the arena again: E01 would otherwise
        // measure a copy per iteration instead of a traversal.
        nuint usedAfterFirst = arenaGraph.Arena.UsedBytes;
        _ = arenaGraph.View;
        _ = arenaGraph.View;

        Assert.Equal(usedAfterFirst, arenaGraph.Arena.UsedBytes);
    }

    [Fact]
    public void Scan_SumsEveryEdge()
    {
        CsrGraph graph = CsrGraph.CreateRandom(8);
        long expected = 0;
        for (int edge = 0; edge < graph.EdgeCount; edge++)
        {
            expected += graph.Targets[edge] + (long)graph.Costs[edge];
        }

        Assert.Equal(expected, CsrTraversal.Scan(graph.View));
    }

    [Fact]
    public void BreadthFirst_ReachesEveryNodeOfTheRing()
    {
        const int nodes = 24;
        CsrGraph graph = CsrGraph.CreateRandom(nodes);
        int[] queue = new int[nodes];
        byte[] discovered = new byte[nodes];

        Assert.Equal(nodes, CsrTraversal.BreadthFirst(graph.View, 0, queue, discovered));
    }

    [Fact]
    public void BreadthFirst_RejectsASourceOutsideTheGraph()
    {
        CsrGraph graph = CsrGraph.CreateRandom(8);
        int[] queue = new int[8];
        byte[] discovered = new byte[8];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CsrTraversal.BreadthFirst(graph.View, 8, queue, discovered));
    }
}
