using System.Runtime.InteropServices;

namespace ArenaBench.Core;

/// <summary>
/// SafeHandle over the arena block (ADR-001 D1/D6, ADR-002 D4).
/// Distinguishes owned memory (allocated and freed here, base is 64-byte
/// aligned via NativeMemory.AlignedAlloc) from an external block (owned and
/// freed by its owner). Safety net for a forgotten Dispose: the SafeHandle
/// finalizer releases owned memory.
/// </summary>
internal sealed class SafeArenaHandle : SafeHandle
{
    private readonly bool _ownsMemory;
    private static int s_liveCount;

    public SafeArenaHandle(nuint size, bool ownsMemory)
        : base(IntPtr.Zero, ownsHandle: true)
    {
        _ownsMemory = ownsMemory;
        Size = size;
        SetHandle(AllocAligned(size));
        Interlocked.Increment(ref s_liveCount);
    }

    public SafeArenaHandle(IntPtr memory, nuint size, bool ownsMemory)
        : base(IntPtr.Zero, ownsHandle: true)
    {
        // A zero handle makes IsInvalid true, so ReleaseHandle would never run -
        // and the live count incremented below would never be decremented. The
        // public Arena constructor checks this too; the guard belongs here as
        // well, because the type is visible to the tests.
        ArgumentOutOfRangeException.ThrowIfEqual(memory, IntPtr.Zero);
        _ownsMemory = ownsMemory;
        Size = size;
        SetHandle(memory);
        Interlocked.Increment(ref s_liveCount);
    }

    /// <summary>Block size in bytes.</summary>
    public nuint Size { get; }

    /// <summary>Base pointer of the block; 64-byte aligned for owned arenas (ADR-001 D1).</summary>
    public IntPtr BasePointer => handle;

    /// <summary>Number of live handles — used by leak tests (ADR-001 D6).</summary>
    public static int LiveCount => s_liveCount;

    private static unsafe IntPtr AllocAligned(nuint size)
    {
        // AlignedAlloc throws OutOfMemoryException when the block cannot be
        // reserved (docs: "Allocating byteCount of memory with alignment
        // failed"), so there is no null result to guard against here, and a
        // null check would be dead code. byteCount 0 is legal and yields a
        // valid pointer that must still be passed to AlignedFree.
        return (IntPtr)NativeMemory.AlignedAlloc(alignment: 64, byteCount: size);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override unsafe bool ReleaseHandle()
    {
        if (_ownsMemory)
        {
            NativeMemory.AlignedFree((void*)handle);
        }

        Interlocked.Decrement(ref s_liveCount);
        return true;
    }
}
