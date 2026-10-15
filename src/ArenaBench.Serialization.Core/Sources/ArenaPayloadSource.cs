using ArenaBench.Core;

namespace ArenaBench.Serialization.Core.Sources;

/// <summary>
/// Arena-backed payload source (concept.md 5.1: arena as one of the sources).
/// The payload is copied once into an arena block with the requested
/// per-allocation alignment (ADR-001 D3), and every view a strategy may want
/// is exposed over that single block.
/// </summary>
public static class ArenaPayloadFactory
{
    /// <summary>Default per-allocation alignment - 16 B.</summary>
    public const int DefaultAlignment = 16;

    /// <summary>Copies <paramref name="payload"/> into an arena block.</summary>
    public static ArenaPayloadSource Create(ReadOnlySpan<byte> payload) => new(payload, DefaultAlignment);

    /// <summary>Copies <paramref name="payload"/> into an arena block with an explicit alignment.</summary>
    public static ArenaPayloadSource Create(ReadOnlySpan<byte> payload, int alignment) => new(payload, alignment);
}

/// <summary>
/// One arena block holding the payload, plus the views the S10-S22 benches use:
/// raw span, UnmanagedMemoryStream and the owning <see cref="Arena"/> for the
/// allocation-side cases (S16, S20). The arena owns the block, so disposing the
/// source frees it and any view obtained from it dies with the arena
/// (ADR-001 D9).
/// </summary>
/// <remarks>
/// The payload always sits behind a one-byte probe allocation, so
/// <see cref="Alignment"/> describes the address the payload really got: modulo
/// 64 that is 1 for align 1 (genuinely unaligned), 16 for align 16 and 0 for
/// align 64. Without the probe every case would be 64-byte aligned, because the
/// block base is, and the alignment axis would measure one and the same layout.
/// </remarks>
public sealed unsafe class ArenaPayloadSource : IDisposable
{
    private readonly Arena _arena;
    private readonly IntPtr _buffer;
    private readonly UnmanagedMemoryStream _stream;

    internal ArenaPayloadSource(ReadOnlySpan<byte> payload, int alignment)
    {
        // The block base is 64-byte aligned (SafeArenaHandle) and a bump arena
        // starts at offset 0, so a first allocation comes out 64-byte aligned
        // whatever it asks for. One throwaway byte pushes the payload to a
        // non-zero offset, which is what makes the alignment real: offset 1 for
        // align 1, offset 16 for align 16, offset 64 for align 64. The extra room
        // covers exactly that worst-case padding.
        _arena = new Arena((nuint)payload.Length + (nuint)alignment, defaultAlign: Math.Max(alignment, 16));
        _arena.AllocPtr(size: 1, align: 1);
        _buffer = _arena.AllocPtr(payload.Length, alignment);
        payload.CopyTo(new Span<byte>((void*)_buffer, payload.Length));

        Alignment = alignment;
        Length = payload.Length;
        _stream = new UnmanagedMemoryStream((byte*)_buffer, payload.Length, payload.Length, FileAccess.Read);
    }

    /// <summary>The arena that owns the block.</summary>
    public Arena Arena => _arena;

    /// <summary>Aligned pointer to the first payload byte.</summary>
    public IntPtr Buffer => _buffer;

    /// <summary>Per-allocation alignment the block was requested with.</summary>
    public int Alignment { get; }

    /// <summary>Payload length in bytes.</summary>
    public int Length { get; }

    /// <summary>Read-only view over the block; valid until the arena is reset or disposed.</summary>
    public ReadOnlySpan<byte> Span => new((void*)_buffer, Length);

    /// <summary>
    /// UnmanagedMemoryStream over the same block - the BCL bridge (S04/S17/S22).
    /// Like <see cref="Span"/> and <see cref="Buffer"/> it lives exactly as long
    /// as the arena: after <see cref="Dispose"/> the memory is gone.
    /// </summary>
    public UnmanagedMemoryStream Stream => _stream;

    /// <summary>Rewinds <see cref="Stream"/> to the first payload byte.</summary>
    public void Rewind() => _stream.Position = 0;

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Dispose();
        _arena.Dispose();
    }
}