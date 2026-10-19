namespace ArenaBench.Core;

/// <summary>
/// Opaque rollback token returned by <see cref="Arena.Mark"/> (ADR-001 D4).
/// Carries the generation and the identity of the arena that issued it, so a
/// mark taken before a <c>Reset</c> - or in another arena - is rejected by
/// <see cref="Arena.Release"/> instead of rolling the offset back over memory
/// that has already been reissued. Out-of-order release is not supported.
/// </summary>
public readonly struct ArenaMark
{
    internal nuint Offset { get; }

    /// <summary>Generation of the issuing arena when the mark was taken (ADR-001 D9).</summary>
    internal uint Generation { get; }

    /// <summary>Identity of the issuing arena - generations of two arenas coincide.</summary>
    internal int ArenaId { get; }

    internal ArenaMark(nuint offset, uint generation, int arenaId)
    {
        Offset = offset;
        Generation = generation;
        ArenaId = arenaId;
    }
}

/// <summary>
/// RAII scope: <see cref="Dispose"/> rolls the arena offset back to the mark
/// captured at creation (ADR-001 D4).
///
/// Dispose is <em>single-shot</em>: the rollback runs first and only then is the
/// scope marked as used, so a <see cref="Arena.Release"/> that throws (stale mark
/// after a <c>Reset</c>, foreign mark, out-of-order release) leaves the scope armed
/// and a second Dispose reports the same failure instead of swallowing it. That is
/// deliberate: a failed rollback means the offset was <em>not</em> restored, and
/// hiding it behind a silent no-op would be worse than a repeated exception. In
/// other words, Dispose is idempotent after a successful rollback and not
/// idempotent in the failure branch - the scope is then spent and the caller is
/// expected to have seen the exception.
///
/// Deliberately a <c>ref struct</c> (like <c>Span&lt;T&gt;</c>): a scope must
/// not outlive the stack frame that owns the arena operations — no boxing,
/// no fields in classes, no crossing <c>async</c>/iterator boundaries, which
/// would silently extend the lifetime of the rollback token.
/// </summary>
public ref struct ArenaScope
{
    private readonly Arena _arena;
    private readonly ArenaMark _mark;
    private bool _disposed;

    internal ArenaScope(Arena arena, ArenaMark mark)
    {
        _arena = arena;
        _mark = mark;
        _disposed = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Release first, then mark the scope as done: a stale mark makes Release
        // throw, and the scope has to stay armed so that a second Dispose reports
        // the same problem instead of silently swallowing it.
        _arena.Release(_mark);
        _disposed = true;
    }
}
