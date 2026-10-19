using ArenaBench.Core;
using Xunit;

namespace ArenaBench.Core.Tests;

// Shares the "Arena" collection with the leak tests: they observe the global
// SafeArenaHandle.LiveCount, so arena-creating tests must stay serialized.
[Collection("Arena")]
public class ThreadLocalArenaTests
{
    [Fact]
    public void Current_IsCreatedOnFirstAccessAndReusedAfterwards()
    {
        using var arenas = new ThreadLocalArena(1024);

        Assert.Equal(0, arenas.LiveArenaCount);
        Arena first = arenas.Current;
        Arena second = arenas.Current;

        Assert.Same(first, second);
        Assert.Equal(1, arenas.LiveArenaCount);
        Assert.Equal(1024u, (uint)first.Size);
    }

    [Fact]
    public void Current_GivesEveryThreadItsOwnArena()
    {
        using var arenas = new ThreadLocalArena(1024);
        Arena main = arenas.Current;

        Arena? fromThread = null;
        Thread thread = new(() => fromThread = arenas.Current);
        thread.Start();
        thread.Join();

        Assert.NotNull(fromThread);
        Assert.NotSame(main, fromThread);
        Assert.Equal(2, arenas.LiveArenaCount);
    }

    [Fact]
    public void Dispose_FreesEveryThreadArena()
    {
        int baseline = LeakProbe.LiveCount();

        ThreadLocalArena arenas = new(1024);
        Arena main = arenas.Current;
        Thread thread = new(() => _ = arenas.Current);
        thread.Start();
        thread.Join();

        Assert.Equal(baseline + 2, SafeArenaHandle.LiveCount);

        arenas.Dispose();

        // Both blocks are gone without waiting for the SafeHandle finalizer, and
        // the wrapper refuses further use.
        Assert.Equal(baseline, SafeArenaHandle.LiveCount);
        Assert.Throws<ObjectDisposedException>(() => arenas.Current);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = main.AllocSpan<byte>(1);
        });
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        ThreadLocalArena arenas = new(1024);
        _ = arenas.Current;
        arenas.Dispose();
        arenas.Dispose();

        Assert.Throws<ObjectDisposedException>(() => arenas.Current);
    }

    [Fact]
    public void AllocationsOfDifferentThreadsAreIndependent()
    {
        using var arenas = new ThreadLocalArena(4096);
        Arena main = arenas.Current;
        _ = main.AllocSpan<byte>(512);
        nuint mainUsed = main.UsedBytes;

        nuint threadUsed = 0;
        Thread thread = new(() =>
        {
            Arena other = arenas.Current;
            _ = other.AllocSpan<byte>(1024);
            threadUsed = other.UsedBytes;
        });
        thread.Start();
        thread.Join();

        Assert.Equal(512u, (uint)mainUsed);
        Assert.Equal(1024u, (uint)threadUsed);
        Assert.Equal(512u, (uint)main.UsedBytes);
    }

}
