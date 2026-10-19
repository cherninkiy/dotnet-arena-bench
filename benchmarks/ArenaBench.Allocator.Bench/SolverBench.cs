using System.Buffers;

using ArenaBench.Allocator.Core.Solver;
using ArenaBench.Core;

using BenchmarkDotNet.Attributes;

namespace ArenaBench.Allocator.Bench;

/// <summary>
/// E01/E02: the CSR of the solver, in managed arrays and in the arena
/// (concept.md 5.2, ADR-001 D3: the auto-alignment API). E01 walks the graph,
/// E02 builds it - the two halves of "arena against <c>int[]</c>", which
/// concept.md 11.2 expects to come out even for the walk and favourably for the
/// build.
/// </summary>
/// <remarks>
/// Both variants walk the very same layout with the very same code
/// (<see cref="CsrTraversal"/>): the only difference is the memory the four flat
/// arrays live in. The arena copy is made once in [GlobalSetup]
/// (<see cref="ArenaGraph"/>), the build cases copy per iteration.
/// </remarks>
public class SolverCsrBench
{
    private CsrGraph _graph = null!;
    private ArenaGraph _arenaGraph = null!;
    private Arena _buildArena = null!;
    private int[] _offsets = null!;
    private int[] _targets = null!;
    private float[] _capacities = null!;
    private float[] _costs = null!;
    private int[] _queue = null!;
    private byte[] _discovered = null!;

    /// <summary>Nodes in the generated ring (extra edges add two rows each).</summary>
    [Params(256, 4096)]
    public int NodeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _graph = CsrGraph.CreateRandom(NodeCount);
        _arenaGraph = new ArenaGraph(_graph);
        _buildArena = new Arena((nuint)ArenaGraph.RequiredBytes(_graph) + 4096);

        _offsets = new int[_graph.Offsets.Length];
        _targets = new int[_graph.Targets.Length];
        _capacities = new float[_graph.Capacities.Length];
        _costs = new float[_graph.Costs.Length];

        _queue = new int[NodeCount];
        _discovered = new byte[NodeCount];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _arenaGraph.Dispose();
        _buildArena.Dispose();
    }

    /// <summary>E01 baseline: row sweep over the managed CSR arrays.</summary>
    [Benchmark(Baseline = true)]
    public long E01_ManagedArray_Scan() => CsrTraversal.Scan(_graph.View);

    /// <summary>E01: the same sweep over the CSR copy in the arena.</summary>
    [Benchmark]
    public long E01_Arena_Scan() => CsrTraversal.Scan(_arenaGraph.View);

    /// <summary>E01: the BFS walk (queue plus discovered map) over the managed CSR.</summary>
    [Benchmark]
    public int E01_ManagedArray_BreadthFirst() =>
        CsrTraversal.BreadthFirst(_graph.View, 0, _queue, _discovered);

    /// <summary>E01: the same BFS over the CSR copy in the arena.</summary>
    [Benchmark]
    public int E01_Arena_BreadthFirst() =>
        CsrTraversal.BreadthFirst(_arenaGraph.View, 0, _queue, _discovered);

    /// <summary>E02 baseline: build the CSR into managed arrays, then read it back.</summary>
    [Benchmark]
    public long E02_ManagedArray_Build()
    {
        _graph.CopyTo(_offsets, _targets, _capacities, _costs);
        return _offsets[0] + _targets[0] + (long)_costs[_costs.Length - 1];
    }

    /// <summary>E02: build the CSR in the arena - one bump per array, no GC object (ADR-001 D7).</summary>
    [Benchmark]
    public long E02_Arena_Build()
    {
        _buildArena.Reset();
        CsrGraphView view = _graph.CopyTo(_buildArena);

        // The same three slots the managed variant reads: the case has to price
        // the build, not an extra traversal of what was just written.
        return view.Offsets[0] + view.Targets[0] + (long)view.Costs[view.Costs.Length - 1];
    }
}

/// <summary>
/// E03: the B&amp;B rollback case of concept.md 5.2 and ADR-001 D4 - the arena
/// against the incumbent <see cref="PathPoolMock"/> (and ArrayPool, for the BCL
/// answer) on one and the same search. Every expansion rents a row buffer and
/// gives it back, so the plug-in of the case is what a rent/return pair costs,
/// and the peak arena usage stays one buffer however deep the search goes.
/// </summary>
/// <remarks>
/// The graph is managed in all three variants: the axis here is the scratch
/// allocator, not where the CSR lives (that is E01). The arena is reset before
/// each solve, which is what a solver tick does between searches.
/// </remarks>
public class SolverRollbackBench
{
    private const int Source = 0;

    private CsrGraph _graph = null!;
    private MiniBranchAndBound _solver = null!;
    private PathPoolMock _pool = null!;
    private Arena _arena = null!;
    private int _sink;

    /// <summary>Nodes in the generated graph; the row scratch grows with the widest row.</summary>
    [Params(256, 4096)]
    public int NodeCount { get; set; }

    /// <summary>Expansions allowed per solve: a shallow and a deep search.</summary>
    [Params(64, 1024)]
    public int NodeBudget { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _graph = CsrGraph.CreateRandom(NodeCount);
        _sink = NodeCount / 2;
        _solver = new MiniBranchAndBound(NodeBudget, maxHops: 8);
        _pool = new PathPoolMock();

        // Rent/Release pair up per node, so a single row is live at any moment:
        // the widest row plus the alignment slack is the whole block the arena
        // needs for the deepest search.
        _arena = new Arena(((nuint)MiniBranchAndBound.ScratchLengthFor(_graph.View) * sizeof(float)) + 64);
    }

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    /// <summary>E03 baseline: the incumbent path pool - a concurrent bag round trip per node.</summary>
    [Benchmark(Baseline = true)]
    public float PathPool_RentReturn()
    {
        var scratch = new PathPoolScratchProvider(_pool);
        return _solver.Solve(_graph.View, Source, _sink, ref scratch);
    }

    /// <summary>E03: Mark/Release - the offset rolls back in O(1) (ADR-001 D4).</summary>
    [Benchmark]
    public float Arena_MarkRelease()
    {
        _arena.Reset();
        var scratch = new ArenaScratchProvider(_arena);
        return _solver.Solve(_graph.View, Source, _sink, ref scratch);
    }

    /// <summary>E03: ArrayPool on the same per-node pattern.</summary>
    [Benchmark]
    public float ArrayPool_RentReturn()
    {
        var scratch = new ArrayPoolScratchProvider(ArrayPool<float>.Shared);
        return _solver.Solve(_graph.View, Source, _sink, ref scratch);
    }
}

/// <summary>
/// E04: parallel B&amp;B (concept.md 5.2, ADR-001 D2) - one batch of independent
/// searches, run by <c>Parallel.For</c> over four scratch strategies: a
/// thread-local arena (no synchronization at all), one shared arena behind a
/// lock (the naive baseline the ADR names), the path pool (thread-safe by
/// design) and ArrayPool (the same).
/// </summary>
/// <remarks>
/// The solver carries per-node state, so every thread needs its own instance -
/// the thread-static field below. Contention shows up in ns/op only; the
/// explicit counters are backlog B-004.
/// </remarks>
public class SolverParallelBench
{
    private const int Source = 0;
    private const int SolveCount = 64;

    [ThreadStatic]
    private static MiniBranchAndBound? t_solver;

    private CsrGraph _graph = null!;
    private ThreadLocalArena _arenas = null!;
    private Arena _sharedArena = null!;
    private PathPoolMock _pool = null!;
    private int _sink;
    private int _nodeBudget;

    /// <summary>Nodes in the generated graph.</summary>
    [Params(1024)]
    public int NodeCount { get; set; }

    /// <summary>Degree of parallelism - and the number of arenas the thread-local path ends up creating.</summary>
    [Params(4)]
    public int Parallelism { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _graph = CsrGraph.CreateRandom(NodeCount);
        _sink = NodeCount / 2;
        _nodeBudget = 128;
        _pool = new PathPoolMock();

        nuint scratchBytes = ((nuint)MiniBranchAndBound.ScratchLengthFor(_graph.View) * sizeof(float)) + 64;
        _arenas = new ThreadLocalArena(scratchBytes);
        _sharedArena = new Arena(scratchBytes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _arenas.Dispose();
        _sharedArena.Dispose();
    }

    /// <summary>E04 baseline: the path pool, thread-safe without any help from the benchmark.</summary>
    [Benchmark(Baseline = true)]
    public int PathPool_Parallel()
    {
        PathPoolMock pool = _pool;
        ParallelOptions options = new() { MaxDegreeOfParallelism = Parallelism };

        Parallel.For(0, SolveCount, options, index =>
        {
            var scratch = new PathPoolScratchProvider(pool);
            _ = SolveOne(_graph, _sink, _nodeBudget, ref scratch);
        });

        return SolveCount;
    }

    /// <summary>E04: a thread-local arena - every thread bumps its own block, no lock anywhere.</summary>
    [Benchmark]
    public int Arena_ThreadLocal_Parallel()
    {
        ThreadLocalArena arenas = _arenas;
        ParallelOptions options = new() { MaxDegreeOfParallelism = Parallelism };

        Parallel.For(0, SolveCount, options, index =>
        {
            Arena arena = arenas.Current;
            arena.Reset(); // one arena per thread serves the whole batch
            var scratch = new ArenaScratchProvider(arena);
            _ = SolveOne(_graph, _sink, _nodeBudget, ref scratch);
        });

        return arenas.LiveArenaCount;
    }

    /// <summary>E04: one shared arena behind a lock - the serialized baseline of ADR-001 D2.</summary>
    [Benchmark]
    public int Arena_SharedLock_Parallel()
    {
        Arena arena = _sharedArena;
        ParallelOptions options = new() { MaxDegreeOfParallelism = Parallelism };

        Parallel.For(0, SolveCount, options, index =>
        {
            lock (arena)
            {
                arena.Reset();
                var scratch = new ArenaScratchProvider(arena);
                _ = SolveOne(_graph, _sink, _nodeBudget, ref scratch);
            }
        });

        return SolveCount;
    }

    /// <summary>E04: ArrayPool, whose shared instance is thread-safe.</summary>
    [Benchmark]
    public int ArrayPool_Parallel()
    {
        ArrayPool<float> pool = ArrayPool<float>.Shared;
        ParallelOptions options = new() { MaxDegreeOfParallelism = Parallelism };

        Parallel.For(0, SolveCount, options, index =>
        {
            var scratch = new ArrayPoolScratchProvider(pool);
            _ = SolveOne(_graph, _sink, _nodeBudget, ref scratch);
        });

        return SolveCount;
    }

    /// <summary>
    /// One solve with the calling thread's solver. The solver holds per-node
    /// state, so sharing one instance across threads would corrupt the search;
    /// the thread static keeps the parallelism in the allocator, which is what
    /// E04 measures.
    /// </summary>
    private static float SolveOne<TScratch>(CsrGraph graph, int sink, int nodeBudget, ref TScratch scratch)
        where TScratch : struct, IScratchProvider
    {
        MiniBranchAndBound solver = t_solver ??= new MiniBranchAndBound(nodeBudget, maxHops: 8);
        return solver.Solve(graph.View, Source, sink, ref scratch);
    }
}

/// <summary>
/// E05: repeated solves of the same graph (concept.md 5.2, "incremental solve",
/// ADR-001 D7) - what it costs to bring the scratch back for the next round:
/// recreating the block, resetting it, reusing a thread-local arena, or going
/// back to a pool.
/// </summary>
/// <remarks>
/// Persistence across process runs is out of scope (backlog B-006): reuse here
/// means reusing the block, not its contents, and the variants differ exactly in
/// what has to be created per round. One solve returns to offset 0 by itself
/// (the per-node Rent/Release pairs up), so a single reset covers the whole batch
/// in the reuse cases.
/// </remarks>
public class SolverIncrementalBench
{
    private const int Source = 0;

    private readonly MiniBranchAndBound _solver = new(nodeBudget: 128, maxHops: 8);

    private CsrGraph _graph = null!;
    private Arena _arena = null!;
    private ThreadLocalArena _arenas = null!;
    private PathPoolMock _pool = null!;
    private nuint _scratchBytes;
    private int _sink;

    /// <summary>Nodes in the generated graph.</summary>
    [Params(256, 4096)]
    public int NodeCount { get; set; }

    /// <summary>Rounds of re-solve on the same graph.</summary>
    [Params(8, 64)]
    public int Rounds { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _graph = CsrGraph.CreateRandom(NodeCount);
        _sink = NodeCount / 2;
        _pool = new PathPoolMock();
        _scratchBytes = ((nuint)MiniBranchAndBound.ScratchLengthFor(_graph.View) * sizeof(float)) + 64;
        _arena = new Arena(_scratchBytes);
        _arenas = new ThreadLocalArena(_scratchBytes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _arena.Dispose();
        _arenas.Dispose();
    }

    /// <summary>E05 baseline: the path pool, rented and returned every round.</summary>
    [Benchmark(Baseline = true)]
    public float PathPool_Reuse()
    {
        float sum = 0;
        for (int round = 0; round < Rounds; round++)
        {
            var scratch = new PathPoolScratchProvider(_pool);
            sum += _solver.Solve(_graph.View, Source, _sink, ref scratch);
        }

        return sum;
    }

    /// <summary>E05: one arena for all rounds - a single reset in front of the batch (ADR-001 D7).</summary>
    [Benchmark]
    public float Arena_Reset()
    {
        _arena.Reset();
        float sum = 0;

        for (int round = 0; round < Rounds; round++)
        {
            var scratch = new ArenaScratchProvider(_arena);
            sum += _solver.Solve(_graph.View, Source, _sink, ref scratch);
        }

        return sum;
    }

    /// <summary>E05: a fresh arena per round - the block is created and freed Rounds times.</summary>
    [Benchmark]
    public float Arena_Recreate()
    {
        float sum = 0;
        for (int round = 0; round < Rounds; round++)
        {
            using var arena = new Arena(_scratchBytes);
            var scratch = new ArenaScratchProvider(arena);
            sum += _solver.Solve(_graph.View, Source, _sink, ref scratch);
        }

        return sum;
    }

    /// <summary>E05: the thread-local wrapper reused across rounds - E04's shape without the parallelism.</summary>
    [Benchmark]
    public float Arena_ThreadLocal_Reuse()
    {
        Arena arena = _arenas.Current;
        arena.Reset();
        float sum = 0;

        for (int round = 0; round < Rounds; round++)
        {
            var scratch = new ArenaScratchProvider(arena);
            sum += _solver.Solve(_graph.View, Source, _sink, ref scratch);
        }

        return sum;
    }

    /// <summary>E05: ArrayPool, rented and returned every round.</summary>
    [Benchmark]
    public float ArrayPool_Reuse()
    {
        ArrayPool<float> pool = ArrayPool<float>.Shared;
        float sum = 0;

        for (int round = 0; round < Rounds; round++)
        {
            var scratch = new ArrayPoolScratchProvider(pool);
            sum += _solver.Solve(_graph.View, Source, _sink, ref scratch);
        }

        return sum;
    }
}
