using ArenaBench.Serialization.Core.Sources;

namespace ArenaBench.Serialization.Tests;

/// <summary>
/// The four sources of concept.md 5.1 have to hand out the same bytes on
/// every pass: one bench iteration reads a whole payload and then starts over
/// through <see cref="IStreamSource.Rewind"/>.
/// </summary>
public sealed class StreamSourceTests
{
    private static readonly byte[] Payload = CreatePayload(1024);

    [Fact]
    public void MemoryStreamSource_RewindRepeatsThePayload() =>
        AssertPayloadRepeats(payload => MemoryStreamFactory.Create(payload));

    [Fact]
    public void FileStreamSource_RewindRepeatsThePayload() =>
        AssertPayloadRepeats(payload => FileStreamFactory.Create(payload));

    [Fact]
    public void NetworkStreamSource_RewindResendsThePayload() =>
        AssertPayloadRepeats(payload => NetworkStreamFactory.CreateLoopback(payload));

    [Fact]
    public void UnmanagedStreamSource_RewindRepeatsThePayload() =>
        AssertPayloadRepeats(payload => UnmanagedStreamFactory.Create(payload));

    [Fact]
    public void UnmanagedStreamSource_HandsOutAlignedMemory()
    {
        using UnmanagedStreamSource source = UnmanagedStreamFactory.Create(Payload);

        Assert.Equal(UnmanagedStreamFactory.DefaultAlignment, source.Alignment);
        Assert.Equal(0, source.Buffer.ToInt64() % UnmanagedStreamFactory.DefaultAlignment);
    }

    [Fact]
    public void FileStreamSource_DeletesItsFileOnDispose()
    {
        string filePath;

        using (FileStreamSource source = FileStreamFactory.Create(Payload))
        {
            filePath = source.FilePath;
            Assert.True(File.Exists(filePath));
        }

        Assert.False(File.Exists(filePath));
    }

    private static void AssertPayloadRepeats(Func<byte[], IStreamSource> create)
    {
        using IStreamSource source = create(Payload);

        Assert.Equal((long)Payload.Length, source.Length);
        Assert.Equal(Payload, ReadAll(source));
        Assert.Equal(Payload, ReadAll(source));
    }

    private static byte[] ReadAll(IStreamSource source)
    {
        byte[] buffer = new byte[source.Length];
        source.Rewind();
        source.Stream.ReadExactly(buffer);
        return buffer;
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 31);
        }

        return payload;
    }
}