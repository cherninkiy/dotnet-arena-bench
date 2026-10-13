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
        void* block = NativeMemory.AlignedAlloc(alignment: 64, byteCount: size);
        if (block == null)
        {
            // AlignedAlloc returns null on OOM (it does not throw): without this
            // check IntPtr.Zero would silently flow into pointer arithmetic.
            throw new OutOfMemoryException($"AlignedAlloc failed for {size} bytes.");
        }

        return (IntPtr)block;
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
