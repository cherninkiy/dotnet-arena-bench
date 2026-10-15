using System.Net;
using System.Net.Sockets;

namespace ArenaBench.Serialization.Core.Sources;

/// <summary>
/// NetworkStream source (S03): a real TCP loopback pair, so the payload
/// travels through the kernel instead of through a managed shim. concept.md
/// 12.1 accepts loopback as the emulation of network traffic.
/// </summary>
public static class NetworkStreamFactory
{
    /// <summary>Lower bound for the loopback buffers, 64 KiB.</summary>
    private const int MinimumBufferSize = 64 * 1024;

    /// <summary>Creates a loopback pair that can hold several payloads in flight.</summary>
    public static NetworkStreamSource CreateLoopback(ReadOnlySpan<byte> payload) => new(payload);

    /// <summary>Buffer size that fits a whole payload with room to spare.</summary>
    internal static int BufferSize(int payloadLength) => Math.Max(payloadLength * 4, MinimumBufferSize);
}

/// <summary>
/// Loopback NetworkStream plus the peer socket that re-sends the payload on
/// every rewind. Both kernel buffers are sized so that one full payload fits
/// into the socket: the peer never has to wait for the reader, which is what
/// allows the send to happen on the measuring thread.
/// </summary>
public sealed class NetworkStreamSource : IStreamSource
{
    private readonly byte[] _payload;
    private readonly NetworkStream _stream;
    private readonly Socket _peer;

    internal NetworkStreamSource(ReadOnlySpan<byte> payload)
    {
        _payload = payload.ToArray();

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(backlog: 1);

        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        client.Connect((IPEndPoint)listener.LocalEndPoint!);

        _peer = listener.Accept();
        _peer.NoDelay = true;

        // SO_SNDBUF/SO_RCVBUF are hints and the OS may clamp them. The buffers
        // are sized for several payloads, so a full payload always fits into the
        // kernel and Rewind() cannot block; a payload beyond the clamped size
        // would need a reader interleaved with the sender.
        int bufferSize = NetworkStreamFactory.BufferSize(_payload.Length);
        _peer.SendBufferSize = bufferSize;
        client.ReceiveBufferSize = bufferSize;

        _stream = new NetworkStream(client, ownsSocket: true);
    }

    /// <inheritdoc />
    public Stream Stream => _stream;

    /// <inheritdoc />
    public long Length => _payload.Length;

    /// <inheritdoc />
    public void Rewind() => SendAll(_peer, _payload);

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Dispose();
        _peer.Dispose();
    }

    private static void SendAll(Socket socket, ReadOnlySpan<byte> payload)
    {
        int sent = 0;
        while (sent < payload.Length)
        {
            int written = socket.Send(payload[sent..], SocketFlags.None);
            if (written <= 0)
            {
                throw new IOException($"Loopback peer stopped accepting data after {sent} bytes.");
            }

            sent += written;
        }
    }
}