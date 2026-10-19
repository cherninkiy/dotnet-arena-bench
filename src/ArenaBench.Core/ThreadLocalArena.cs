using System.Collections.Concurrent;

namespace ArenaBench.Core;

/// <summary>
/// Per-thread arena for parallel scenarios (ADR-001 D2): every thread that asks
/// for <see cref="Current"/> gets its own <see cref="Arena"/> of the same size,
/// so a parallel loop needs no synchronization around its scratch at all.
/// <para>
/// The wrapper is a <see cref="ThreadLocal{T}"/> value plus its own registry of
/// created arenas, rather than a <c>[ThreadStatic]</c> field or
/// <see cref="ThreadLocal{T}.Values"/> (<c>trackAllValues: true</c>): a
/// thread-static field cannot be per-instance and offers no way to enumerate the
/// arenas at <see cref="Dispose"/>, and the registry keeps the lifetime of the
/// references explicit - they are dropped exactly as the arenas are freed, and
/// <see cref="LiveArenaCount"/> still answers after <see cref="Dispose"/> instead
/// of throwing. Holding a reference to every created arena until
/// <see cref="Dispose"/> is the point, not a leak: that is what makes the arenas
/// of already exited threads reachable and freeable, since nothing else points at
/// them. The price is one <see cref="ThreadLocal{T}.Value"/> lookup per
/// <see cref="Current"/> access (E04 measures it), not per allocation.
/// </para>
/// <para>
/// Thread-local storage beats a shared arena here: a shared bump arena cannot
/// roll back (<see cref="Arena.Release"/>) or reset while another thread is
/// allocating, so E04 has to emulate it with a lock - which is exactly the
/// baseline the thread-local arena is measured against (ADR-001 D2).
/// </para>
/// </summary>
public sealed class ThreadLocalArena : IDisposable
{
    private readonly ThreadLocal<Arena> _arenas;

    // Every arena this wrapper has created, in creation order. The registry is
    // what frees the arenas of threads that have already exited: a completed
    // thread's ThreadLocal value is reachable from nowhere else, so without this
    // list its block would only come back in the SafeArenaHandle finalizer.
    private readonly ConcurrentQueue<Arena> _created = new();

    // 0 = alive, 1 = disposed. An int plus Interlocked, not a bool: Dispose has to
    // be single-shot even when two threads call it at once, and Current has to see
    // the flag with a barrier.
    private int _disposed;

    /// <summary>
    /// Creates the wrapper; no arena is allocated until a thread touches
    /// <see cref="Current"/>.
    /// </summary>
    /// <param name="size">Block size of every per-thread arena.</param>
    /// <param name="defaultAlign">Alignment contract of every per-thread arena (ADR-001 D3).</param>
    public ThreadLocalArena(nuint size, int defaultAlign = 16)
    {
        Size = size;
        DefaultAlign = defaultAlign;
        _arenas = new ThreadLocal<Arena>(CreateArena);
    }

    /// <summary>Block size of every per-thread arena.</summary>
    public nuint Size { get; }

    /// <summary>Alignment contract of every per-thread arena (ADR-001 D3).</summary>
    public int DefaultAlign { get; }

    /// <summary>The calling thread's arena, created on first access.</summary>
    public Arena Current
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _arenas.Value!;
        }
    }

    /// <summary>
    /// Number of arenas created and not yet freed, that is: of threads that
    /// touched <see cref="Current"/>. Used by the tests and by E04 to report how
    /// many threads took part.
    /// </summary>
    public int LiveArenaCount => _created.Count;

    /// <summary>
    /// Frees every per-thread arena created so far and empties the registry. Other
    /// threads must have stopped using the wrapper: an arena created concurrently
    /// with (or after) this call is not in the snapshot, so it falls back to the
    /// <see cref="SafeArenaHandle"/> finalizer and only then leaves
    /// <c>LiveCount</c>. Calling Dispose twice, or from several threads at once,
    /// is a no-op after the first call.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _arenas.Dispose();

        while (_created.TryDequeue(out Arena? arena))
        {
            arena.Dispose();
        }
    }

    private Arena CreateArena()
    {
        Arena arena = new(Size, DefaultAlign);
        _created.Enqueue(arena);
        return arena;
    }
}
