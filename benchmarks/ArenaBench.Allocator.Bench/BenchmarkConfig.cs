using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace ArenaBench.Allocator.Bench;

/// <summary>
/// Standard BDN config + diagnosers (concept.md, 7.3/7.4). The job is
/// not pinned here: CI passes --job short via the command line.
/// </summary>
public static class BenchmarkConfig
{
    public static IConfig Instance { get; } = ManualConfig.Create(DefaultConfig.Instance)
        .AddDiagnoser(MemoryDiagnoser.Default)
        .AddDiagnoser(ThreadingDiagnoser.Default);
}
