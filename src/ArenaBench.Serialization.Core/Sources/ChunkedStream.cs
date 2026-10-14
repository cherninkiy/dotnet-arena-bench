namespace ArenaBench.Serialization.Core.Sources;

/// <summary>
/// Chunked source (V07/V08, concept.md 5.1: network delivery boundaries are
/// arbitrary). The wrapper never returns more than <c>maxChunk</c> bytes from
/// a single <see cref="Stream.Read(Span{byte})"/>, so a frame regularly
/// straddles two reads and the reader has to carry the remainder - which is
/// exactly what the carry benches measure, and why the chunk size is fixed
/// instead of left to the OS.
/// </summary>
public static class ChunkedStreamFactory
{
    /// <summary>Default delivery boundary - 13 B, deliberately smaller than any frame header plus payload.</summary>
    public const int DefaultMaxChunk = 13;

    /// <summary>Wraps <paramref name="payload"/> in a rewound chunked stream.</summary>
    public static ChunkedStream Create(ReadOnlySpan<byte> payload) => Create(payload, DefaultMaxChunk);

    /// <summary>Wraps <paramref name="payload"/> in a rewound chunked stream with an explicit chunk size.</summary>
    public static ChunkedStream Create(ReadOnlySpan<byte> payload, int maxChunk)
    {
        var inner = new MemoryStream(payload.ToArray(), writable: false);
        return new ChunkedStream(inner, maxChunk);
    }
}

/// <summary>Seekable stream that caps the size of every read.</summary>
public sealed class ChunkedStream : Stream
{
    private readonly Stream _inner;
    private readonly int _maxChunk;

    internal ChunkedStream(Stream inner, int maxChunk)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunk);

        _inner = inner;
        _maxChunk = maxChunk;
    }

    /// <summary>Largest number of bytes a single read can return.</summary>
    public int MaxChunk => _maxChunk;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) =>
        _inner.Read(buffer[..Math.Min(buffer.Length, _maxChunk)]);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, Math.Min(count, _maxChunk));

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}