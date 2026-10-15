using System.Buffers;
using System.Buffers.Binary;
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
/// <remarks>
/// Frame headers are little-endian on the wire: <see cref="BinaryReader"/> is
/// little-endian by definition, and <see cref="Primitives"/> takes the order
/// explicitly, while <see cref="MemoryMarshalRead"/>/<see cref="UnsafeReadUnaligned"/>
/// rely on the native (little-endian) order of every platform the PoC targets.
/// <see cref="Pipelines"/> and <see cref="WithCarry"/> are little-endian only:
/// a big-endian frame variant would need its own strategy rather than a flag,
/// so it is stated here instead of being discovered from a wrong number.
/// </remarks>
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

    /// <summary>
    /// V04: PipeReader/SequenceReader over a stream. The header is read with
    /// <c>TryReadLittleEndian</c>, i.e. this strategy is little-endian only -
    /// the T1 wire order (see the class remarks).
    /// </summary>
    public static long Pipelines(Stream stream, int frameCount)
    {
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            return ReadPipedFrames(reader, frameCount);
        }
        finally
        {
            // PipeReader is not IDisposable: Complete() is what releases its
            // buffered segments, and it has to run even when a frame is missing.
            reader.Complete();
        }
    }

    private static long ReadPipedFrames(PipeReader reader, int frameCount)
    {
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

                // Peek the length before consuming anything: a frame whose header
                // straddles two segments must not be dropped on the next refill,
                // and UnreadSpan alone is short in exactly that case.
                if (!TryPeekPayloadLength(ref sequence, out int payloadLength))
                {
                    break;
                }

                int frameSize = FrameFormat.HeaderSize + payloadLength;
                if (sequence.Remaining < frameSize)
                {
                    break;
                }

                sequence.Advance(sizeof(int));
                sequence.TryReadLittleEndian(out short kindRaw); // SequenceReader exposes only the signed 16-bit overload
                sequence.TryRead(out byte codec);
                sequence.TryRead(out byte flags);
                sum += FrameFormat.ChecksumOf(payloadLength, (ushort)kindRaw, codec, flags);
                sequence.Advance(payloadLength);
                frames++;
            }

            // Examined = everything that is buffered. Saying so is what makes the
            // reader refill from the stream instead of handing the same segment
            // back forever when a frame is still incomplete.
            reader.AdvanceTo(sequence.Position, result.Buffer.End);
            if (frames < frameCount && result.IsCompleted)
            {
                throw new EndOfStreamException($"Pipe completed after {frames} of {frameCount} frames.");
            }
        }

        return sum;
    }

    /// <summary>
    /// Peeks the little-endian payload length without consuming it. The
    /// contiguous run is the fast path (one read, no per-byte calls); a header
    /// that straddles two segments falls back to four <c>TryPeek</c> calls, which
    /// is cheap and, unlike <c>UnreadSpan</c>, never reports a short header.
    /// </summary>
    private static bool TryPeekPayloadLength(ref SequenceReader<byte> sequence, out int payloadLength)
    {
        ReadOnlySpan<byte> unread = sequence.UnreadSpan;
        if (unread.Length >= sizeof(int))
        {
            payloadLength = BinaryPrimitives.ReadInt32LittleEndian(unread);
            return true;
        }

        Span<byte> raw = stackalloc byte[sizeof(int)];
        for (int i = 0; i < raw.Length; i++)
        {
            if (!sequence.TryPeek(i, out byte value))
            {
                payloadLength = 0;
                return false;
            }

            raw[i] = value;
        }

        payloadLength = BinaryPrimitives.ReadInt32LittleEndian(raw);
        return true;
    }

    /// <summary>
    /// V07/V08: chunked stream read through a caller-owned carry buffer
    /// (<paramref name="carry"/> must hold the longest frame: 136 B). The header
    /// is read little-endian - the T1 wire order (see the class remarks).
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