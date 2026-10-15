namespace ArenaBench.Core;

/// <summary>
/// Opaque rollback token returned by <see cref="Arena.Mark"/> (ADR-001 D4).
/// Out-of-order release is not supported.
/// </summary>
public readonly struct ArenaMark
{
    internal nuint Offset { get; }

    internal ArenaMark(nuint offset) => Offset = offset;
}

/// <summary>
/// RAII scope: <see cref="Dispose"/> rolls the arena offset back to the mark
/// captured at creation (ADR-001 D4).
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

        _disposed = true;
        _arena.Release(_mark);
    }
}
