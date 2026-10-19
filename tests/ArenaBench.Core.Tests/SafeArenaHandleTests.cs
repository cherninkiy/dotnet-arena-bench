using System.Runtime.InteropServices;
using ArenaBench.Core;
using Xunit;

namespace ArenaBench.Core.Tests;

// Shares the "Arena" collection with the leak tests: they observe the global
// SafeArenaHandle.LiveCount, so arena-creating tests must stay serialized.
[Collection("Arena")]
public class SafeArenaHandleTests
{
    [Fact]
    public void ZeroPointer_IsRejected()
    {
        // A zero handle would make IsInvalid true, so ReleaseHandle would never run
        // and the live count would leak the increment the constructor already did.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SafeArenaHandle(IntPtr.Zero, 64, ownsMemory: false));
    }

    [Fact]
    public void ZeroPointer_DoesNotMoveTheLiveCount()
    {
        int baseline = LeakProbe.LiveCount();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SafeArenaHandle(IntPtr.Zero, 64, ownsMemory: false));

        Assert.Equal(baseline, SafeArenaHandle.LiveCount);
    }

    [Fact]
    public void ZeroSizedOwnedBlock_IsAcceptedAndDisposed()
    {
        // The arena hands out zero-sized allocations, so a zero-size block has to
        // survive construction and disposal without touching the live count.
        int baseline = LeakProbe.LiveCount();

        using (var handle = new SafeArenaHandle(0, ownsMemory: true))
        {
            Assert.Equal(0u, (uint)handle.Size);
            Assert.Equal(baseline + 1, SafeArenaHandle.LiveCount);
        }

        Assert.Equal(baseline, SafeArenaHandle.LiveCount);
    }
}
