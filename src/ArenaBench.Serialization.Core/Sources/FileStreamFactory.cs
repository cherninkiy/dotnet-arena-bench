namespace ArenaBench.Serialization.Core.Sources;

/// <summary>
/// FileStream source (S02): a real file in the temp directory. The payload
/// never outlives the run (<see cref="FileOptions.DeleteOnClose"/>), and the
/// OS page cache is part of what gets measured - concept.md 12.1 treats that
/// as an accepted property of the file source.
/// </summary>
public static class FileStreamFactory
{
    /// <summary>Default buffer size of the stream, 4 KiB.</summary>
    public const int DefaultBufferSize = 4096;

    /// <summary>Writes <paramref name="payload"/> into a temporary file and opens it for reading.</summary>
    public static FileStreamSource Create(ReadOnlySpan<byte> payload) => new(payload, DefaultBufferSize);

    /// <summary>Same as <see cref="Create(ReadOnlySpan{byte})"/> with an explicit stream buffer size.</summary>
    public static FileStreamSource Create(ReadOnlySpan<byte> payload, int bufferSize) => new(payload, bufferSize);
}

/// <summary>Rewindable FileStream over a temporary payload file.</summary>
public sealed class FileStreamSource : IStreamSource
{
    private readonly FileStream _stream;

    internal FileStreamSource(ReadOnlySpan<byte> payload, int bufferSize)
    {
        FilePath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"arenabench-t2-{Guid.NewGuid():N}.bin");

        _stream = new FileStream(
            FilePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        _stream.Write(payload);
        _stream.Flush();
        _stream.Position = 0;
    }

    /// <summary>Path of the temporary file; the file is deleted when the source is disposed.</summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public Stream Stream => _stream;

    /// <inheritdoc />
    public long Length => _stream.Length;

    /// <inheritdoc />
    public void Rewind() => _stream.Position = 0;

    /// <inheritdoc />
    public void Dispose() => _stream.Dispose();
}