using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArenaBench.Core;

/// <summary>
/// Per-allocation <see cref="MemoryManager{T}"/> over unmanaged arena memory
/// (ADR-001 D9). The manager does NOT track outstanding rentals: the caller
/// must keep it (and its <see cref="Memory{T}"/>) alive no longer than the
/// owning arena. Reset/Dispose of the arena invalidates the memory —
/// using it afterwards is undefined behavior.
/// </summary>
public sealed unsafe class ArenaMemoryManager<T> : MemoryManager<T> where T : unmanaged
{
    private readonly IntPtr _pointer;
    private readonly int _count;

    public ArenaMemoryManager(IntPtr pointer, int count)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pointer, IntPtr.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _pointer = pointer;
        _count = count;
    }

    /// <summary>Number of elements in the managed view.</summary>
    public int Count => _count;

    /// <inheritdoc />
    public override Span<T> GetSpan() => new((void*)_pointer, _count);

    /// <summary>
    /// Unmanaged memory is always pinned. Bounds-checked: a negative or
    /// out-of-range <paramref name="elementIndex"/> would otherwise produce a
    /// pointer outside the block. One-past-end (<c>index == Count</c>) stays
    /// legal, as in the BCL <see cref="MemoryManager{T}.Pin"/> contract.
    /// </summary>
    public override MemoryHandle Pin(int elementIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(elementIndex, _count);

        // The offset is computed in nint: elementIndex * sizeof(T) in int would
        // wrap for a wide T before Count could ever get that large.
        nint byteOffset = (nint)elementIndex * (nint)Unsafe.SizeOf<T>();
        return new MemoryHandle((byte*)_pointer + byteOffset);
    }

    /// <inheritdoc />
    public override void Unpin()
    {
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
    }
}
