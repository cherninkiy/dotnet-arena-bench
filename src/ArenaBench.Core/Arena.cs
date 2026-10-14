using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArenaBench.Core;

/// <summary>
/// Bump-pointer arena over a single unmanaged block (ADR-001).
/// Single-threaded, fixed size, never grows; overflow throws
/// <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class Arena : IDisposable
{
    private readonly SafeArenaHandle _handle;
    private readonly int _defaultAlign;
    private nuint _offset;
    private bool _disposed;

    /// <summary>
    /// Creates an arena that owns a single unmanaged block with a
    /// 64-byte aligned base (ADR-001 D1: NativeMemory.AlignedAlloc(64, size)).
    /// </summary>
    /// <param name="size">Block size in bytes.</param>
    /// <param name="defaultAlign">
    /// Default alignment for auto-aligned allocations (ADR-001 D3);
    /// must be a positive power of two.
    /// </param>
    public Arena(nuint size, int defaultAlign = 16)
    {
        ValidateAlign(defaultAlign);
        _handle = new SafeArenaHandle(size, ownsMemory: true);
        _defaultAlign = defaultAlign;
    }

    /// <summary>
    /// Wraps an external unmanaged block, e.g. a mmap view (ADR-002 D4).
    /// Base alignment is guaranteed by the memory owner.
    /// If <paramref name="ownsMemory"/> is false, the memory is freed
    /// by its owner, not by the arena.
    /// </summary>
    public Arena(IntPtr memory, nuint size, bool ownsMemory = false, int defaultAlign = 16)
    {
        ValidateAlign(defaultAlign);
        ArgumentOutOfRangeException.ThrowIfEqual(memory, IntPtr.Zero);
        _handle = new SafeArenaHandle(memory, size, ownsMemory);
        _defaultAlign = defaultAlign;
    }

    /// <summary>Total block size in bytes (ADR-001 D8).</summary>
    public nuint Size => _handle.Size;

    /// <summary>Bytes allocated by the arena so far.</summary>
    public nuint UsedBytes => _offset;

    /// <summary>Bytes left before overflow (ADR-001 D8: check before hot-path allocation).</summary>
    public nuint Free => _handle.Size - _offset;

    /// <summary>Captures the current offset for a later <see cref="Release"/> (ADR-001 D4).</summary>
    public ArenaMark Mark() => new(_offset);

    /// <summary>
    /// RAII scope over <see cref="Mark"/>/<see cref="Release"/> (ADR-001 D4):
    /// Dispose rolls the offset back to the mark. A <see cref="Reset"/> between
    /// the scope and its disposal invalidates the mark, so the rollback throws
    /// <see cref="ArgumentException"/> (out-of-order use is rejected) instead
    /// of restoring an offset the arena no longer has.
    /// </summary>
    public ArenaScope Scope() => new(this, Mark());

    /// <summary>Rolls the offset back to a previously captured mark (ADR-001 D4).</summary>
    public void Release(ArenaMark mark)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (mark.Offset > _offset)
        {
            throw new ArgumentException("Mark is newer than the current arena state.", nameof(mark));
        }

        _offset = mark.Offset;
    }

    /// <summary>
    /// Returns the offset to zero without freeing memory (ADR-001 D7: O(1)).
    /// WARNING (ADR-001 D9): invalidates all previously issued
    /// Memory/Span/IntPtr — using them afterwards is undefined behavior.
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _offset = 0;
    }

    /// <summary>
    /// Allocates a non-zeroed <see cref="Span{T}"/> (ADR-001 D5), aligned to
    /// max(defaultAlign, natural alignment of primitives) (ADR-001 D3). For
    /// composite unmanaged types the BCL exposes no AlignOf API
    /// (dotnet/runtime #2167): defaultAlign is their alignment contract and
    /// must be >= natural alignment of T; explicit
    /// <see cref="AllocBytes(int,int)"/> covers special cases.
    /// count 0 returns an empty span over a valid pointer, as the BCL does for
    /// zero-sized allocations; the alignment padding may still advance
    /// <see cref="UsedBytes"/>.
    /// </summary>
    public Span<T> AllocSpan<T>(int count) where T : unmanaged
    {
        IntPtr pointer = AllocPtr<T>(count);
        return AsSpan<T>(pointer, count);
    }

    /// <summary>Allocates a zeroed <see cref="Span{T}"/> (ADR-001 D5).</summary>
    public Span<T> AllocSpanZeroed<T>(int count) where T : unmanaged
    {
        Span<T> span = AllocSpan<T>(count);
        span.Clear();
        return span;
    }

    /// <summary>
    /// Allocates count elements of T, auto-aligned to max(defaultAlign,
    /// natural alignment of primitives) (ADR-001 D3); raw pointer variant.
    /// count 0 accepts no bytes and returns a valid pointer that must not be
    /// dereferenced (the BCL convention for zero-sized allocations).
    /// </summary>
    public IntPtr AllocPtr<T>(int count) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        uint typeAlign = PrimitiveAlignOf<T>();
        nuint byteCount = checked((nuint)count * (nuint)Unsafe.SizeOf<T>());
        return Allocate(byteCount, Math.Max((uint)_defaultAlign, typeAlign));
    }

    /// <summary>Allocates size bytes with explicit alignment (ADR-001 D3).</summary>
    public Span<byte> AllocBytes(int size, int align)
    {
        IntPtr pointer = AllocPtr(size, align);
        return AsSpan<byte>(pointer, size);
    }

    /// <summary>Allocates size bytes with explicit alignment (ADR-001 D3); raw pointer variant.</summary>
    public IntPtr AllocPtr(int size, int align)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ValidateAlign(align);
        return Allocate((nuint)size, (uint)align);
    }

    /// <summary>
    /// Allocates a <see cref="Memory{T}"/> view (ADR-001 D9): the memory manager is
    /// created per allocation and is NOT tracked by the arena — Reset/Dispose
    /// invalidates it (undefined behavior). count 0 yields an empty view whose
    /// <c>Pin</c> is still valid.
    /// </summary>
    public Memory<T> AllocMemory<T>(int count) where T : unmanaged
    {
        IntPtr pointer = AllocPtr<T>(count);
        return new ArenaMemoryManager<T>(pointer, count).Memory;
    }

    /// <summary>
    /// Frees the owned block (if any). Same invalidation caveat as
    /// <see cref="Reset"/> (ADR-001 D9).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }

    /// <summary>
    /// Natural alignment of primitive types (JIT-constant per T). Composite
    /// unmanaged types return 0: the BCL exposes no AlignOf API
    /// (dotnet/runtime #2167), so defaultAlign is their alignment contract
    /// (ADR-001 D3).
    /// </summary>
    private static uint PrimitiveAlignOf<T>() where T : unmanaged
        => typeof(T) switch
        {
            Type t when t == typeof(byte) || t == typeof(sbyte) || t == typeof(bool) => 1,
            Type t when t == typeof(short) || t == typeof(ushort) || t == typeof(char) => 2,
            Type t when t == typeof(int) || t == typeof(uint) || t == typeof(float) => 4,
            Type t when t == typeof(long) || t == typeof(ulong) || t == typeof(double)
                || t == typeof(IntPtr) || t == typeof(UIntPtr) => 8,
            _ => 0,
        };

    private static void ValidateAlign(int align)
    {
        if (align <= 0 || (align & (align - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(align), align, "Alignment must be a positive power of two.");
        }
    }

    private static unsafe Span<T> AsSpan<T>(IntPtr pointer, int count) where T : unmanaged
        => new(pointer.ToPointer(), count);

    private IntPtr Allocate(nuint size, uint align)
    {
        nuint mask = align - 1;

        // Padding first: a wrap here would silently produce a small offset, so
        // instead of relying on nuint arithmetic the wrap is rejected.
        nuint padded = _offset + mask;
        if (padded < _offset)
        {
            throw new InvalidOperationException(
                $"Arena overflow: {align}-byte padding for {size} bytes does not fit the address space.");
        }

        nuint alignedOffset = padded & ~mask;

        // Subtraction instead of addition: the sum cannot wrap, so a huge
        // request always reports an arena overflow - it can neither bypass the
        // check nor surface as an OverflowException from a `checked` context.
        if (alignedOffset > _handle.Size || size > _handle.Size - alignedOffset)
        {
            throw new InvalidOperationException(
                $"Arena overflow: need {size} bytes at offset {alignedOffset} with {align}-byte alignment, "
                + $"size is {_handle.Size}.");
        }

        _offset = alignedOffset + size;
        return (IntPtr)((nint)_handle.BasePointer + (nint)alignedOffset);
    }
}

