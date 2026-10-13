using System.Runtime.CompilerServices;
using ArenaBench.Core;
using Xunit;

namespace ArenaBench.Core.Tests;

// SafeArenaHandle.LiveCount is process-global: the leak tests must not run
// concurrently with anything that creates arenas (xunit parallelizes test
// collections by default), otherwise the baseline drifts and the assertions
// become flaky.
[Collection("Arena")]
public class ArenaLeakTests
{
    [Fact]
    public void ForgottenDispose_IsReclaimedBySafeHandleFinalizer()
    {
        CollectAndWaitForFinalizers();
        int baseline = SafeArenaHandle.LiveCount;

        CreateUndisposedArenas(500);

        CollectAndWaitForFinalizers();
        Assert.Equal(baseline, SafeArenaHandle.LiveCount);
    }

    // Deliberately not inlined: the allocating frame must be gone before the
    // collection, otherwise the last temporary can stay rooted in a dead stack
    // slot and the assertion becomes flaky (ADR-001 D6 — no Dispose anywhere).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateUndisposedArenas(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ = new Arena(256);
        }
    }

    [Fact]
    public void Dispose_DecrementsLiveHandleCount()
    {
        CollectAndWaitForFinalizers();
        int baseline = SafeArenaHandle.LiveCount;

        for (int i = 0; i < 100; i++)
        {
            using var arena = new Arena(256);
        }

        CollectAndWaitForFinalizers();
        Assert.Equal(baseline, SafeArenaHandle.LiveCount);
    }

    private static void CollectAndWaitForFinalizers(int attempts = 20)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
