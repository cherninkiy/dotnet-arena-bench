using System.Runtime.InteropServices;

namespace ArenaBench.Serialization.Core.Structures;

/// <summary>
/// T2 video record, 64 B: a 28 B header plus a 36 B bulk payload, i.e. one
/// cache line per record (concept.md, 5.1). The fixed buffer keeps the
/// record unmanaged and padding-free.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VideoRecord64
{
    /// <summary>Unix time in ticks - 8 B.</summary>
    public long Timestamp;

    /// <summary>Frame sequence number - 8 B.</summary>
    public long Sequence;

    /// <summary>Frame identifier - 4 B.</summary>
    public int FrameId;

    /// <summary>Frame width in pixels - 2 B.</summary>
    public ushort Width;

    /// <summary>Frame height in pixels - 2 B.</summary>
    public ushort Height;

    /// <summary>Codec identifier - 1 B.</summary>
    public byte Codec;

    /// <summary>Bit flags - 1 B.</summary>
    public byte Flags;

    /// <summary>Alignment filler, keeps the layout explicit - 2 B.</summary>
    public ushort Reserved;

    /// <summary>Bulk frame bytes - 36 B, never parsed by the strategies.</summary>
    public fixed byte Payload[36];
}