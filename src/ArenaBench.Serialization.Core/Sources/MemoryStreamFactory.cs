namespace ArenaBench.Serialization.Core.Sources;

/// <summary>
/// MemoryStream source (S01): the managed baseline. The payload is copied
/// once into a <c>byte[]</c>, and a rewind is nothing but resetting
/// <see cref="MemoryStream.Position"/> - the cheapest source of the matrix.
/// </summary>
public static class MemoryStreamFactory
{
    /// <summary>Wraps a managed copy of <paramref name="payload"/> in a rewound stream.</summary>
    public static MemoryStreamSource Create(ReadOnlySpan<byte> payload) => new(payload);
}

/// <summary>Rewindable read-only MemoryStream over a managed payload copy.</summary>
public sealed class MemoryStreamSource : IStreamSource
{
    private readonly MemoryStream _stream;

    internal MemoryStreamSource(ReadOnlySpan<byte> payload)
    {
        _stream = new MemoryStream(payload.ToArray(), writable: false);
    }

    /// <inheritdoc />
    public Stream Stream => _stream;

    /// <inheritdoc />
    public long Length => _stream.Length;

    /// <inheritdoc />
    public void Rewind() => _stream.Position = 0;

    /// <inheritdoc />
    public void Dispose() => _stream.Dispose();
}