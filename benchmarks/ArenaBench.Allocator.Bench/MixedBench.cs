using ArenaBench.Allocator.Core.Workload;
using ArenaBench.Core;

using BenchmarkDotNet.Attributes;

namespace ArenaBench.Allocator.Bench;

/// <summary>
/// M01-M04: the mixed workload of concept.md 5.2 - three commodity types
/// (voice, service, text) processed in one interleaved pass, with per-type
/// arenas against one shared arena, on a balanced mix and on a skewed one.
/// </summary>
/// <remarks>
/// The 2x2 is the point: M01/M02 answer "do per-type arenas beat one arena"
/// (concept.md 11.2 expects a yes for the mixed case), and M03/M04 repeat the
/// question when one type dominates, where a shared arena has to hold padding
/// for a type that is barely used. Each pass resets its arenas and then writes
/// and reads every item, so the numbers are steady state, not first-touch.
/// </remarks>
public class MixedBench
{
    private MixedWorkload _balanced = null!;
    private MixedWorkload _skewed = null!;

    private Arena _voice = null!;
    private Arena _service = null!;
    private Arena _text = null!;
    private Arena _shared = null!;

    private Arena _voiceSkewed = null!;
    private Arena _serviceSkewed = null!;
    private Arena _textSkewed = null!;
    private Arena _sharedSkewed = null!;

    /// <summary>Items per pass (concept.md 5.2 sizes: 24/32/44 B payloads).</summary>
    [Params(WorkloadGenerator.DefaultItemCount)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _balanced = WorkloadGenerator.Create(ItemCount, MixedWorkload.BalancedTextShare);
        _skewed = WorkloadGenerator.Create(ItemCount, MixedWorkload.SkewedTextShare);

        // Per-type arenas are sized for their own type; the shared one holds the
        // whole schedule. Both get the same slack, so the comparison is about the
        // layout, not about who has room to spare.
        _voice = new Arena(_balanced.ArenaBytesFor(CommodityType.Voice));
        _service = new Arena(_balanced.ArenaBytesFor(CommodityType.Service));
        _text = new Arena(_balanced.ArenaBytesFor(CommodityType.Text));
        _shared = new Arena(_balanced.TotalArenaBytes());

        _voiceSkewed = new Arena(_skewed.ArenaBytesFor(CommodityType.Voice));
        _serviceSkewed = new Arena(_skewed.ArenaBytesFor(CommodityType.Service));
        _textSkewed = new Arena(_skewed.ArenaBytesFor(CommodityType.Text));
        _sharedSkewed = new Arena(_skewed.TotalArenaBytes());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _voice.Dispose();
        _service.Dispose();
        _text.Dispose();
        _shared.Dispose();
        _voiceSkewed.Dispose();
        _serviceSkewed.Dispose();
        _textSkewed.Dispose();
        _sharedSkewed.Dispose();
    }

    /// <summary>M01 baseline: three per-type arenas, balanced mix.</summary>
    [Benchmark(Baseline = true)]
    public long M01_PerTypeArenas_Balanced()
    {
        _voice.Reset();
        _service.Reset();
        _text.Reset();
        return Process(_balanced, _voice, _service, _text);
    }

    /// <summary>M02: one shared arena for the same balanced mix - the 3-vs-1 case.</summary>
    [Benchmark]
    public long M02_SharedArena_Balanced()
    {
        _shared.Reset();
        return Process(_balanced, _shared, _shared, _shared);
    }

    /// <summary>M03: per-type arenas when text takes 80% of the work - interference.</summary>
    [Benchmark]
    public long M03_PerTypeArenas_Skewed()
    {
        _voiceSkewed.Reset();
        _serviceSkewed.Reset();
        _textSkewed.Reset();
        return Process(_skewed, _voiceSkewed, _serviceSkewed, _textSkewed);
    }

    /// <summary>M04: the shared arena under the same skew - the crowded baseline.</summary>
    [Benchmark]
    public long M04_SharedArena_Skewed()
    {
        _sharedSkewed.Reset();
        return Process(_skewed, _sharedSkewed, _sharedSkewed, _sharedSkewed);
    }

    /// <summary>
    /// One pass over the schedule: every item writes its payload into the arena
    /// its type owns and reads the last byte back. The code is the same for the
    /// per-type and the shared variants, including the type switch - the only
    /// difference is which arena the switch returns.
    /// </summary>
    private static long Process(MixedWorkload workload, Arena voice, Arena service, Arena text)
    {
        long sum = 0;
        ReadOnlySpan<CommodityType> schedule = workload.Schedule;

        for (int index = 0; index < schedule.Length; index++)
        {
            CommodityType type = schedule[index];
            ReadOnlySpan<byte> template = workload.Template(type);
            Arena arena = type switch
            {
                CommodityType.Voice => voice,
                CommodityType.Service => service,
                _ => text,
            };

            Span<byte> slot = arena.AllocSpan<byte>(template.Length);
            template.CopyTo(slot);
            sum += slot[template.Length - 1];
        }

        return sum;
    }
}
