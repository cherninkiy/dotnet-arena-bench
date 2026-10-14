using System.Runtime.InteropServices;

namespace ArenaBench.Serialization.Core.Sources;

/// <summary>
/// UnmanagedMemoryStream source (S04): the bridge from unmanaged memory to
/// the BCL stream APIs. The payload lives in an aligned
/// <see cref="NativeMemory.AlignedAlloc"/> block and is exposed as a stream
/// without any managed copy - the same memory the arena hands out on Day 2.
/// </summary>
public static class UnmanagedStreamFactory
{
    /// <summary>Default block alignment - 64 B, one cache line (ADR-001 D3).</summary>
    public const int DefaultAlignment = 64;

    /// <summary>Copies <paramref name="payload"/> into an aligned unmanaged block.</summary>
    public static UnmanagedStreamSource Create(ReadOnlySpan<byte> payload) => new(payload, DefaultAlignment);

    /// <summary>Same as <see cref="Create(ReadOnlySpan{byte})"/> with an explicit alignment.</summary>
    public static UnmanagedStreamSource Create(ReadOnlySpan<byte> payload, int alignment) => new(payload, alignment);
}

/// <summary>Rewindable UnmanagedMemoryStream over an owned, aligned unmanaged block.</summary>
public sealed unsafe class UnmanagedStreamSource : IStreamSource
{
    private readonly UnmanagedMemoryStream _stream;
    private byte* _buffer;

    internal UnmanagedStreamSource(ReadOnlySpan<byte> payload, int alignment)
    {
        // aligned_alloc (POSIX) requires a size that is a multiple of the
        // alignment, so the block is rounded up instead of taken verbatim.
        // AlignedAlloc throws OutOfMemoryException on failure (it never returns
        // null), so a failed reservation cannot reach the pointer arithmetic
        // below; no local null check is needed for symmetry with SafeHandle.
        //
        // Unlike the arena path this call goes straight to the CRT, so the
        // alignment is honoured as an allocation alignment - and it can only be
        // honoured down to what the platform guarantees (malloc alignment, 8 B on
        // x64). A truly "tight" (unaligned) layout therefore cannot be allocated
        // at all; it only exists as an offset inside a block, which is why the
        // alignment axis is modelled in ArenaPayloadSource.
        nuint size = RoundUp((nuint)payload.Length, (nuint)alignment);
        _buffer = (byte*)NativeMemory.AlignedAlloc(size, (nuint)alignment);
        payload.CopyTo(new Span<byte>(_buffer, payload.Length));

        Alignment = alignment;
        _stream = new UnmanagedMemoryStream(_buffer, payload.Length, payload.Length, FileAccess.Read);
    }

    /// <summary>Alignment the block was allocated with.</summary>
    public int Alignment { get; }

    /// <summary>Pointer to the first payload byte - aligned as requested.</summary>
    public IntPtr Buffer => (IntPtr)_buffer;

    /// <inheritdoc />
    public Stream Stream => _stream;

    /// <inheritdoc />
    public long Length => _stream.Length;

    /// <inheritdoc />
    public void Rewind() => _stream.Position = 0;

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Dispose();
        if (_buffer is not null)
        {
            NativeMemory.AlignedFree(_buffer);
            _buffer = null;
        }
    }

    private static nuint RoundUp(nuint value, nuint alignment) =>
        (value + alignment - 1) / alignment * alignment;
}