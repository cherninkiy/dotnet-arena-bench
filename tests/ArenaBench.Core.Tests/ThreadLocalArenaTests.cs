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
    public void Dispose_IsSingleShotUnderConcurrency()
    {
        ThreadLocalArena arenas = new(1024);
        _ = arenas.Current;

        // Without the Interlocked guard every caller would walk the snapshot and
        // dispose the same arenas, and the second pass would hit a disposed
        // ThreadLocal.
        Parallel.For(0, 8, _ => arenas.Dispose());

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

    [Fact]
    public void Dispose_FreesTheArenasOfManyShortLivedThreads()
    {
        const int threads = 1000;
        int baseline = LeakProbe.LiveCount();

        ThreadLocalArena arenas = new(512);

        for (int i = 0; i < threads; i++)
        {
            Thread thread = new(() => _ = arenas.Current);
            thread.Start();
            thread.Join();
        }

        // One arena per thread, held by the wrapper's own registry - that is what
        // makes the arenas of threads that have already exited reachable at all.
        Assert.Equal(threads, arenas.LiveArenaCount);
        Assert.Equal(baseline + threads, SafeArenaHandle.LiveCount);

        arenas.Dispose();

        // Every one of them is freed outright (none is left to the finalizer), and
        // the registry drops its references as it goes.
        Assert.Equal(baseline, SafeArenaHandle.LiveCount);
        Assert.Equal(0, arenas.LiveArenaCount);
    }
}
