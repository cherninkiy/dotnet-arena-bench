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
/// source frees it and any span obtained from it dies with the arena
/// (ADR-001 D9).
/// </summary>
public sealed unsafe class ArenaPayloadSource : IDisposable
{
    private readonly Arena _arena;
    private readonly IntPtr _buffer;
    private readonly UnmanagedMemoryStream _stream;

    internal ArenaPayloadSource(ReadOnlySpan<byte> payload, int alignment)
    {
        // Room for the payload plus the worst-case padding of the alignment.
        _arena = new Arena((nuint)payload.Length + (nuint)alignment, defaultAlign: Math.Max(alignment, 16));
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

    /// <summary>UnmanagedMemoryStream over the same block - the BCL bridge (S04/S17/S22).</summary>
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