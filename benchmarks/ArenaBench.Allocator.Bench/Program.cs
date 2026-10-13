using ArenaBench.Allocator.Bench;
using BenchmarkDotNet.Running;

// Without --filter BDN prompts for an interactive benchmark selection,
// which hangs CI and redirected runs. Default to running all benchmarks.
string[] effectiveArgs = args.Any(a => a.StartsWith("--filter", StringComparison.OrdinalIgnoreCase))
    ? args
    : args.Append("--filter").Append("*").ToArray();

BenchmarkSwitcher.FromAssembly(typeof(BenchmarkConfig).Assembly).Run(effectiveArgs, BenchmarkConfig.Instance);
