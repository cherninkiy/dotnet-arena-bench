using ArenaBench.Serialization.Core.Reading;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// T1 frame format (concept.md 5.1: binary stream, variable-length): an 8 B
/// header - payload length, kind, codec, flags - followed by the payload
/// bytes. Frames are the variable-length point of the size axis, so there is
/// no single stride: readers consume one frame at a time, and the payload
/// length they parse is what tells them where the next frame starts.
/// </summary>
public static class FrameFormat
{
    /// <summary>Wire size of the frame header - 8 B.</summary>
    public const int HeaderSize = 8;

    /// <summary>Smallest payload - 16 B.</summary>
    public const int MinPayloadSize = 16;

    /// <summary>Largest payload - 128 B; the longest frame is 136 B.</summary>
    public const int MaxPayloadSize = 128;

    /// <summary>Deterministic payload length of a frame: 16..128 B in 16 B steps.</summary>
    public static int PayloadSize(int frameIndex) => MinPayloadSize + (frameIndex % 8) * 16;

    /// <summary>Full wire size of the frame with the given index.</summary>
    public static int FrameSize(int frameIndex) => HeaderSize + PayloadSize(frameIndex);

    /// <summary>Total wire size of <paramref name="frameCount"/> frames.</summary>
    public static int TotalSize(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        int total = 0;
        for (int i = 0; i < frameCount; i++)
        {
            total += FrameSize(i);
        }

        return total;
    }

    /// <summary>Deterministic header of the frame with the given index.</summary>
    public static FrameHeader Create(int frameIndex) => new()
    {
        PayloadLength = PayloadSize(frameIndex),
        Kind = (ushort)(1 + (frameIndex % 4)),
        Codec = (byte)(8 + (frameIndex % 4)),
        Flags = (byte)(frameIndex % 2),
    };

    /// <summary>
    /// Encodes one frame - header and payload - into <paramref name="destination"/>,
    /// which must be <see cref="FrameSize"/> bytes long.
    /// </summary>
    public static void WriteFrame(in FrameHeader header, Span<byte> destination, ByteOrder order)
    {
        FieldCodec.WriteInt32(destination, header.PayloadLength, order);
        FieldCodec.WriteUInt16(destination[4..], header.Kind, order);
        destination[6] = header.Codec;
        destination[7] = header.Flags;
        FieldCodec.WritePayloadRamp(destination[HeaderSize..], header.PayloadLength);
    }

    /// <summary>
    /// Reads the header fields of one frame and reports the parsed payload
    /// length, which is what the caller needs to reach the next frame.
    /// </summary>
    public static long ReadPrimitives(ReadOnlySpan<byte> source, ByteOrder order, out int payloadLength)
    {
        payloadLength = FieldCodec.ReadInt32(source, order);
        ushort kind = FieldCodec.ReadUInt16(source[4..], order);
        return ChecksumOf(payloadLength, kind, source[6], source[7]);
    }

    /// <summary>Reads the header fields of one frame.</summary>
    public static long ReadPrimitives(ReadOnlySpan<byte> source, ByteOrder order) =>
        ReadPrimitives(source, order, out _);

    /// <summary>
    /// V01/S22: reads one frame with <see cref="BinaryReader"/> and drains the
    /// payload region into <paramref name="scratch"/> (at least
    /// <see cref="MaxPayloadSize"/> bytes), because no primitive read consumes it.
    /// </summary>
    public static long ReadBinaryReader(BinaryReader reader, Span<byte> scratch)
    {
        int payloadLength = reader.ReadInt32();
        ushort kind = reader.ReadUInt16();
        byte codec = reader.ReadByte();
        byte flags = reader.ReadByte();

        RecordBinaryReader.ReadExactly(reader, scratch[..payloadLength]);
        return ChecksumOf(payloadLength, kind, codec, flags);
    }

    /// <summary>
    /// Reports the full size of the frame that starts at <paramref name="available"/>,
    /// or <c>false</c> when the header is not fully available yet - the
    /// condition a streaming reader has to wait for.
    /// </summary>
    public static bool TryGetFrameSize(ReadOnlySpan<byte> available, ByteOrder order, out int frameSize, out int payloadLength)
    {
        if (available.Length < HeaderSize)
        {
            frameSize = 0;
            payloadLength = 0;
            return false;
        }

        int length = FieldCodec.ReadInt32(available, order);
        if (length < 0 || length > MaxPayloadSize)
        {
            // A length outside the declared range is not a frame this format
            // describes: fail fast instead of skipping a nonsense number of bytes
            // (or waiting for a frame that can never arrive).
            throw new InvalidDataException(
                $"Frame payload length {length} is outside the supported range 0..{MaxPayloadSize}.");
        }

        payloadLength = length;
        frameSize = HeaderSize + payloadLength;
        return true;
    }

    /// <summary>Order-independent fingerprint of a frame header.</summary>
    public static long Checksum(in FrameHeader header) =>
        ChecksumOf(header.PayloadLength, header.Kind, header.Codec, header.Flags);

    /// <summary>Fingerprint mix of the four header fields, in wire order.</summary>
    public static long ChecksumOf(int payloadLength, ushort kind, byte codec, byte flags)
    {
        long hash = payloadLength;
        hash = RecordFingerprint.Mix(hash, kind);
        hash = RecordFingerprint.Mix(hash, codec);
        hash = RecordFingerprint.Mix(hash, flags);
        return hash;
    }

    /// <summary>Encodes <paramref name="frameCount"/> frames into a fresh byte image.</summary>
    public static byte[] CreatePayload(int frameCount, ByteOrder order)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        byte[] payload = new byte[TotalSize(frameCount)];
        int offset = 0;
        for (int i = 0; i < frameCount; i++)
        {
            FrameHeader header = Create(i);
            int frameSize = FrameSize(i);
            WriteFrame(in header, payload.AsSpan(offset, frameSize), order);
            offset += frameSize;
        }

        return payload;
    }

    /// <summary>Fingerprint of the generated frames: what every strategy has to reproduce.</summary>
    public static long Fingerprint(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        long sum = 0;
        for (int i = 0; i < frameCount; i++)
        {
            FrameHeader header = Create(i);
            sum += Checksum(in header);
        }

        return sum;
    }
}