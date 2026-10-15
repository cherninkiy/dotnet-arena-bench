using ArenaBench.Allocator.Bench;
using BenchmarkDotNet.Running;

// Without --filter BDN prompts for an interactive benchmark selection,
// which hangs CI and redirected runs. Default to running all benchmarks;
// an explicit --filter (or --filter=...) is forwarded untouched.
string[] effectiveArgs = HasFilter(args)
    ? args
    : args.Append("--filter").Append("*").ToArray();

BenchmarkSwitcher.FromAssembly(typeof(BenchmarkConfig).Assembly).Run(effectiveArgs, BenchmarkConfig.Instance);

static bool HasFilter(string[] arguments) => arguments.Any(argument =>
    argument.Equals("--filter", StringComparison.OrdinalIgnoreCase)
    || argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase));
