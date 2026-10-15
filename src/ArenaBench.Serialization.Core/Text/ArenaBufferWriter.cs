using System.Buffers;

using ArenaBench.Core;

namespace ArenaBench.Serialization.Core.Text;

/// <summary>
/// IBufferWriter&lt;byte&gt; over arena memory - the bridge Utf8JsonWriter needs
/// (concept.md 5.1 and 12.2). The arena block is allocated once and
/// <see cref="Reset"/> starts a new message in the same block, so the measured
/// path contains no managed buffer at all.
/// </summary>
/// <remarks>
/// <para>
/// GetMemory/GetSpan hand out the remaining space of the block and
/// <see cref="Advance"/> tracks how much of it is used. This is a fixed-capacity
/// writer, not a growing one: <see cref="IBufferWriter{T}"/> allows an
/// implementation to fail when a hint cannot be satisfied, but then the caller
/// has to guarantee that the value fits. Utf8JsonWriter grows its window with the
/// value being written, so serializing a document that does not fit throws in the
/// middle - size the arena for the largest message, or use
/// <c>ArrayBufferWriter&lt;byte&gt;</c> when the size is unknown.
/// </para>
/// <para>
/// The hint is checked for every window, so the writer is exactly as forgiving as
/// the arena block is large: the failure mode is deliberate, not an artefact.
/// </para>
/// </remarks>
public sealed class ArenaBufferWriter : IBufferWriter<byte>, IDisposable
{
    private readonly ArenaMemoryManager<byte> _manager;
    private readonly Memory<byte> _memory;
    private readonly int _capacity;
    private int _written;

    /// <summary>Takes an arena block of <paramref name="capacity"/> bytes as the writer's buffer.</summary>
    public ArenaBufferWriter(Arena arena, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _manager = new ArenaMemoryManager<byte>(arena.AllocPtr<byte>(capacity), capacity, arena);
        _memory = _manager.Memory;
        _capacity = capacity;
    }

    /// <summary>Bytes written since the last <see cref="Reset"/>.</summary>
    public int WrittenCount => _written;

    /// <summary>Total capacity of the arena block.</summary>
    public int Capacity => _capacity;

    /// <inheritdoc />
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > _capacity - _written)
        {
            throw new InvalidOperationException(
                $"Advance({count}) exceeds the arena buffer capacity {_capacity}.");
        }

        _written += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureHint(sizeHint);
        return _memory[_written..];
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureHint(sizeHint);
        return _memory.Span[_written..];
    }

    /// <summary>Starts a new message in the same block: no allocation, no zeroing.</summary>
    public void Reset() => _written = 0;

    /// <summary>The bytes written since the last <see cref="Reset"/>.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _memory.Span[.._written];

    /// <summary>
    /// The manager owns nothing - the arena owns the block - so this only ends
    /// the IMemoryOwner contract. MemoryManager&lt;T&gt; implements IDisposable
    /// explicitly, hence the interface call.
    /// </summary>
    public void Dispose() => ((IDisposable)_manager).Dispose();

    /// <summary>
    /// Fails when the next window cannot be satisfied. IBufferWriter&lt;T&gt; does not
    /// require an implementation to grow its buffer, but it does require the
    /// implementation to be explicit about it: this is the single place where that
    /// contract is enforced, and it runs for GetMemory and GetSpan alike.
    /// </summary>
    private void EnsureHint(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        if (sizeHint > _capacity - _written)
        {
            throw new InvalidOperationException(
                $"Arena buffer has {_capacity - _written} bytes left, but {sizeHint} were requested.");
        }
    }
}