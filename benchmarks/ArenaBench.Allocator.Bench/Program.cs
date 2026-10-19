using ArenaBench.Allocator.Bench;
using ArenaBench.Allocator.Core.Boundaries;
using BenchmarkDotNet.Running;
using System.Runtime.InteropServices;

// Without --filter BDN prompts for an interactive benchmark selection,
// which hangs CI and redirected runs. Default to running all benchmarks;
// an explicit --filter (or --filter=...) is forwarded untouched.
string[] effectiveArgs = HasFilter(args)
    ? args
    : args.Append("--filter").Append("*").ToArray();

// One line before the run, so CI logs and artifacts say which platform produced
// them and whether the raw libc exports resolved there: that path is best-effort
// (NativeMethods), and an unavailable one is otherwise visible only as an
// exception in the middle of the boundary series.
Console.WriteLine(
    $"arena-bench: {RuntimeInformation.RuntimeIdentifier}, {RuntimeInformation.FrameworkDescription}, "
    + $"raw libc exports: {(PInvokeScenarios.RawExportsAvailable ? "available" : "unavailable")}");

BenchmarkSwitcher.FromAssembly(typeof(BenchmarkConfig).Assembly).Run(effectiveArgs, BenchmarkConfig.Instance);

static bool HasFilter(string[] arguments) => arguments.Any(argument =>
    argument.Equals("--filter", StringComparison.OrdinalIgnoreCase)
    || argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase));
