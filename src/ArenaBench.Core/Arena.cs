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
    private static int s_nextId;

    private readonly SafeArenaHandle _handle;
    private readonly int _defaultAlign;

    // Identity of this arena, handed to every mark it issues. Without it a mark
    // from another arena looks perfectly valid here (generations of two arenas
    // coincide, the offset is in range) and would roll back an unrelated block.
    private readonly int _id;
    private nuint _offset;
    private uint _generation;
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
        _id = Interlocked.Increment(ref s_nextId);
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
        _id = Interlocked.Increment(ref s_nextId);
        _handle = new SafeArenaHandle(memory, size, ownsMemory);
        _defaultAlign = defaultAlign;
    }

    /// <summary>Total block size in bytes (ADR-001 D8).</summary>
    public nuint Size => _handle.Size;

    /// <summary>Bytes allocated by the arena so far.</summary>
    public nuint UsedBytes => _offset;

    /// <summary>Bytes left before overflow (ADR-001 D8: check before hot-path allocation).</summary>
    public nuint Free => _handle.Size - _offset;

    /// <summary>
    /// Bumped by <see cref="Reset"/> and <see cref="Dispose"/> (ADR-001 D9).
    /// Views created through <see cref="AllocMemory{T}"/> remember the generation
    /// they were created with and reject use afterwards, so a stale view fails
    /// loudly instead of quietly reading memory that now belongs to another
    /// allocation. Raw <see cref="Span{T}"/> and <see cref="IntPtr"/> keep the
    /// documented undefined behavior: they cannot be checked.
    /// </summary>
    public uint Generation => _generation;

    /// <summary>
    /// Captures the current offset together with the generation and the identity
    /// of this arena, for a later <see cref="Release"/> (ADR-001 D4).
    /// </summary>
    public ArenaMark Mark() => new(_offset, _generation, _id);

    /// <summary>
    /// RAII scope over <see cref="Mark"/>/<see cref="Release"/> (ADR-001 D4):
    /// Dispose rolls the offset back to the mark. A <see cref="Reset"/> between
    /// the scope and its disposal invalidates the mark, so the rollback throws
    /// <see cref="ArgumentException"/> (out-of-order use is rejected) instead
    /// of restoring an offset the arena no longer has.
    /// </summary>
    public ArenaScope Scope() => new(this, Mark());

    /// <summary>
    /// Rolls the offset back to a previously captured mark (ADR-001 D4).
    /// A mark equal to the current offset is a legal no-op. Everything else is
    /// rejected with <see cref="ArgumentException"/>: a mark that is newer than
    /// the arena state (out-of-order release), a mark taken before a
    /// <see cref="Reset"/> of this arena, and a mark taken in a different arena.
    /// The last two would otherwise move the offset back over memory that has
    /// already been reissued - the ADR-001 D9 corruption the generation counter
    /// exists to catch.
    /// </summary>
    public void Release(ArenaMark mark)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (mark.ArenaId != _id)
        {
            throw new ArgumentException(
                "Mark belongs to a different arena: releasing it here would roll back an unrelated block.",
                nameof(mark));
        }

        if (mark.Generation != _generation)
        {
            throw new ArgumentException(
                "Mark was taken before the arena was reset: the memory it points at has been reissued.",
                nameof(mark));
        }

        if (mark.Offset > _offset)
        {
            throw new ArgumentException("Mark is newer than the current arena state.", nameof(mark));
        }

        _offset = mark.Offset;
    }

    /// <summary>
    /// Returns the offset to zero without freeing memory (ADR-001 D7: O(1)).
    /// WARNING (ADR-001 D9): invalidates all previously issued Memory/Span/IntPtr.
    /// <see cref="Memory{T}"/> views from <see cref="AllocMemory{T}"/> detect it
    /// and throw <see cref="InvalidOperationException"/>; using a raw
    /// <see cref="Span{T}"/> or <see cref="IntPtr"/> afterwards is undefined behavior.
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _offset = 0;
        _generation++;
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
    /// PRECONDITION for composite T: the arena cannot verify that defaultAlign
    /// covers the natural alignment of T (no AlignOf API, dotnet/runtime #2167),
    /// so an under-aligned defaultAlign is silently wrong for such a type - it is
    /// a contract, not a runtime check.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    /// Same composite-T precondition as <see cref="AllocSpan{T}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    /// Allocates a <see cref="Memory{T}"/> view (ADR-001 D9). The arena does not
    /// track outstanding rentals, but the view validates the arena generation on
    /// every access, so using it after <see cref="Reset"/>/<see cref="Dispose"/>
    /// throws <see cref="InvalidOperationException"/> instead of reading memory
    /// that has been reissued. count 0 yields an empty view whose <c>Pin</c> is
    /// still valid.
    /// </summary>
    public Memory<T> AllocMemory<T>(int count) where T : unmanaged
    {
        IntPtr pointer = AllocPtr<T>(count);
        return new ArenaMemoryManager<T>(pointer, count, this).Memory;
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
        _generation++;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        // The pointer arithmetic runs in nint. On a 32-bit process the cast would
        // wrap for an offset above nint.MaxValue, but such a block cannot be
        // reserved there in the first place, so the 64-bit path is the only
        // reachable one.
        _offset = alignedOffset + size;
        return (IntPtr)((nint)_handle.BasePointer + (nint)alignedOffset);
    }
}

