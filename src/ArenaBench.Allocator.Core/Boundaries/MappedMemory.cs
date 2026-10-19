using System.IO.MemoryMappedFiles;

using ArenaBench.Core;

namespace ArenaBench.Allocator.Core.Boundaries;

/// <summary>
/// I08: shared memory through the BCL (ADR-002 D4) - a file mapping whose view
/// the arena can run on top of. The mapping owns the memory, the arena wraps the
/// pointer and must not free it: that is the <c>ownsMemory: false</c> case
/// ADR-001 D1 added for this scenario.
/// </summary>
public sealed unsafe class MappedMemory : IDisposable
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _accessor;
    private byte* _pointer;
    private bool _disposed;

    /// <summary>Creates an anonymous mapping of <paramref name="size"/> bytes and acquires its pointer.</summary>
    public MappedMemory(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        _file = MemoryMappedFile.CreateNew(null, size);
        _accessor = _file.CreateViewAccessor(0, size, MemoryMappedFileAccess.ReadWrite);

        byte* pointer = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        // The view can start at an offset inside the mapping; the accessor knows it.
        _pointer = pointer + _accessor.PointerOffset;
        Size = size;
    }

    /// <summary>Mapping size in bytes.</summary>
    public int Size { get; }

    /// <summary>Pointer to the first mapped byte.</summary>
    public IntPtr Pointer => (IntPtr)_pointer;

    /// <summary>The BCL view, for the comparison against writing through the arena.</summary>
    public MemoryMappedViewAccessor Accessor => _accessor;

    /// <summary>
    /// An arena over the mapping. It does not own the memory: disposing the
    /// mapping is what releases it (ADR-001 D1).
    /// </summary>
    public Arena CreateArena(int defaultAlign = 16)
        => new(Pointer, (nuint)Size, ownsMemory: false, defaultAlign: defaultAlign);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        _accessor.Dispose();
        _file.Dispose();
        _pointer = null;
    }
}
