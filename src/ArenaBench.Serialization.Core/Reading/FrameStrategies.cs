using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Core.Reading;

/// <summary>
/// T1 frame strategies (concept.md 5.1: Span.Slice, Unsafe.ReadUnaligned,
/// Pipelines, streaming with carry). Variable-length frames are consumed one
/// at a time - the payload length parsed from the header is what advances a
/// reader - and every strategy returns the summed frame fingerprint, so the
/// numbers are comparable with T2 and across sources.
/// </summary>
public static class FrameStrategies
{
    /// <summary>V02/V05/V06/S21: field-by-field walk with BinaryPrimitives over a span.</summary>
    public static long Primitives(ReadOnlySpan<byte> payload, int frameCount, ByteOrder order)
    {
        long sum = 0;
        int offset = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            sum += FrameFormat.ReadPrimitives(payload[offset..], order, out int payloadLength);
            offset += FrameFormat.HeaderSize + payloadLength;
        }

        return sum;
    }

    /// <summary>V03: one header struct copy per frame via MemoryMarshal.Read.</summary>
    public static long MemoryMarshalRead(ReadOnlySpan<byte> payload, int frameCount)
    {
        long sum = 0;
        int offset = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            FrameHeader header = MemoryMarshal.Read<FrameHeader>(payload.Slice(offset, FrameFormat.HeaderSize));
            sum += FrameFormat.Checksum(in header);
            offset += FrameFormat.HeaderSize + header.PayloadLength;
        }

        return sum;
    }

    /// <summary>V03: one header struct read per frame via Unsafe.ReadUnaligned, no copy.</summary>
    public static long UnsafeReadUnaligned(ReadOnlySpan<byte> payload, int frameCount)
    {
        long sum = 0;
        int offset = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            ref byte origin = ref MemoryMarshal.GetReference(payload.Slice(offset, FrameFormat.HeaderSize));
            FrameHeader header = Unsafe.ReadUnaligned<FrameHeader>(ref origin);
            sum += FrameFormat.Checksum(in header);
            offset += FrameFormat.HeaderSize + header.PayloadLength;
        }

        return sum;
    }

    /// <summary>V01/S22: BinaryReader over a stream; the payload is drained through <paramref name="scratch"/>.</summary>
    public static long BinaryReader(BinaryReader reader, int frameCount, Span<byte> scratch)
    {
        long sum = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            sum += FrameFormat.ReadBinaryReader(reader, scratch);
        }

        return sum;
    }

    /// <summary>V04: PipeReader/SequenceReader over a stream; frames are little-endian.</summary>
    public static long Pipelines(Stream stream, int frameCount)
    {
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        long sum = 0;
        int frames = 0;

        while (frames < frameCount)
        {
            ReadResult result = reader.ReadAsync().AsTask().GetAwaiter().GetResult();
            var sequence = new SequenceReader<byte>(result.Buffer);

            while (frames < frameCount)
            {
                if (sequence.Remaining < FrameFormat.HeaderSize)
                {
                    break;
                }

                // Peek the length first: consuming a header before the whole
                // frame is buffered would lose it on the next refill.
                if (!FrameFormat.TryGetFrameSize(sequence.UnreadSpan, ByteOrder.LittleEndian, out int frameSize, out _)
                    || sequence.Remaining < frameSize)
                {
                    break;
                }

                sequence.TryReadLittleEndian(out int payloadLength);
                sequence.TryReadLittleEndian(out short kindRaw); // SequenceReader exposes only the signed 16-bit overload
                ushort kind = (ushort)kindRaw;
                sequence.TryRead(out byte codec);
                sequence.TryRead(out byte flags);
                sum += FrameFormat.ChecksumOf(payloadLength, kind, codec, flags);
                sequence.Advance(payloadLength);
                frames++;
            }

            reader.AdvanceTo(sequence.Position);
            if (frames < frameCount && result.IsCompleted)
            {
                throw new EndOfStreamException($"Pipe completed after {frames} of {frameCount} frames.");
            }
        }

        reader.Complete();
        return sum;
    }

    /// <summary>
    /// V07/V08: chunked stream read through a caller-owned carry buffer
    /// (<paramref name="carry"/> must hold the longest frame: 136 B).
    /// </summary>
    public static long WithCarry(Stream stream, Span<byte> carry, int frameCount)
    {
        if (carry.Length < FrameFormat.HeaderSize + FrameFormat.MaxPayloadSize)
        {
            throw new ArgumentException(
                $"Carry buffer must hold the longest frame ({FrameFormat.HeaderSize + FrameFormat.MaxPayloadSize} bytes).",
                nameof(carry));
        }

        int available = 0;
        int consumed = 0;
        long sum = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            int frameSize;
            while (!IsFrameReady(carry, consumed, available, out frameSize))
            {
                // Compact the partial frame to the front, then read more: this
                // is the carry step the streaming pattern is about.
                if (consumed > 0)
                {
                    carry[consumed..available].CopyTo(carry);
                    available -= consumed;
                    consumed = 0;
                }

                int read = stream.Read(carry[available..]);
                if (read == 0)
                {
                    throw new EndOfStreamException($"Stream ended after {frame} of {frameCount} frames.");
                }

                available += read;
            }

            sum += FrameFormat.ReadPrimitives(carry.Slice(consumed, FrameFormat.HeaderSize), ByteOrder.LittleEndian);
            consumed += frameSize;
        }

        return sum;
    }

    /// <summary>
    /// True when a whole frame is buffered at <paramref name="consumed"/>. The
    /// carry loop advances only once the payload bytes have arrived, so a frame
    /// is never half consumed and half still in flight.
    /// </summary>
    private static bool IsFrameReady(ReadOnlySpan<byte> carry, int consumed, int available, out int frameSize) =>
        FrameFormat.TryGetFrameSize(carry[consumed..available], ByteOrder.LittleEndian, out frameSize, out _)
        && consumed + frameSize <= available;
}