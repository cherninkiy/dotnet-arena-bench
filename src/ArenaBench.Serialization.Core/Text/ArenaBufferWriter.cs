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
/// GetMemory/GetSpan hand out the remaining space of the block. Utf8JsonWriter
/// asks for a size hint before it flushes a window: a hint that no longer fits
/// means the fixed-size arena is exhausted, and that fails loudly rather than
/// silently growing a managed buffer.
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

        _manager = new ArenaMemoryManager<byte>(arena.AllocPtr<byte>(capacity), capacity);
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

    /// <summary>
    /// The manager owns nothing - the arena owns the block - so this only ends
    /// the IMemoryOwner contract. MemoryManager&lt;T&gt; implements IDisposable
    /// explicitly, hence the interface call.
    /// </summary>
    public void Dispose() => ((IDisposable)_manager).Dispose();

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