using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArenaBench.Core;

/// <summary>
/// Per-allocation <see cref="MemoryManager{T}"/> over unmanaged arena memory
/// (ADR-001 D9). The manager does NOT track outstanding rentals: the caller must
/// keep it (and its <see cref="Memory{T}"/>) alive no longer than the owning
/// arena.
/// <para>
/// When the owner is known (the usual case - <c>Arena.AllocMemory&lt;T&gt;</c>
/// passes it), the view validates the arena generation on every access:
/// a view used after <c>Reset</c>/<c>Dispose</c> throws
/// <see cref="InvalidOperationException"/> instead of quietly reading memory that
/// has been reissued. The check is one field comparison per view access - not per
/// element - so it stays off the measured inner loop.
/// </para>
/// count 0 is legal and describes an empty view.
/// </summary>
public sealed unsafe class ArenaMemoryManager<T> : MemoryManager<T> where T : unmanaged
{
    private readonly IntPtr _pointer;
    private readonly int _count;
    private readonly Arena? _owner;
    private readonly uint _generation;

    /// <summary>Creates a view without an owner: no staleness detection is possible.</summary>
    public ArenaMemoryManager(IntPtr pointer, int count)
        : this(pointer, count, owner: null)
    {
    }

    /// <summary>
    /// Creates a view over arena memory. <paramref name="owner"/> is remembered so
    /// that a later <c>Reset</c>/<c>Dispose</c> of that arena invalidates the view
    /// loudly (ADR-001 D9).
    /// </summary>
    public ArenaMemoryManager(IntPtr pointer, int count, Arena? owner)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pointer, IntPtr.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _pointer = pointer;
        _count = count;
        _owner = owner;
        _generation = owner?.Generation ?? 0;
    }

    /// <summary>Number of elements in the managed view.</summary>
    public int Count => _count;

    /// <inheritdoc />
    public override Span<T> GetSpan()
    {
        ThrowIfStale();
        return new Span<T>((void*)_pointer, _count);
    }

    /// <summary>
    /// Unmanaged memory is always pinned. Bounds-checked: a negative or
    /// out-of-range <paramref name="elementIndex"/> would otherwise produce a
    /// pointer outside the block. One-past-end (<c>index == Count</c>) stays
    /// legal, as in the BCL <see cref="MemoryManager{T}.Pin"/> contract.
    /// </summary>
    public override MemoryHandle Pin(int elementIndex)
    {
        ThrowIfStale();
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

    private void ThrowIfStale()
    {
        if (_owner is not null && _owner.Generation != _generation)
        {
            throw new InvalidOperationException(
                "This view was created before the arena was reset or disposed: reading it would touch "
                + "memory that has been reissued (ADR-001 D9). Re-allocate the view.");
        }
    }
}
