using System.Runtime.InteropServices;

namespace ArenaBench.Serialization.Core.Structures;

/// <summary>
/// T2 extended record, 128 B: a 36 B header plus a 92 B bulk payload. Two
/// cache lines per record - the size where the source and alignment axes of
/// PoC-1 start to matter (concept.md, 5.1).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ExtendedRecord128
{
    /// <summary>Unix time in ticks - 8 B.</summary>
    public long Timestamp;

    /// <summary>Frame sequence number - 8 B.</summary>
    public long Sequence;

    /// <summary>Flow identifier - 4 B.</summary>
    public int FlowId;

    /// <summary>Source node of the flow (CSR index) - 4 B.</summary>
    public int SourceNode;

    /// <summary>Target node of the flow (CSR index) - 4 B.</summary>
    public int TargetNode;

    /// <summary>Frame width in pixels - 2 B.</summary>
    public ushort Width;

    /// <summary>Frame height in pixels - 2 B.</summary>
    public ushort Height;

    /// <summary>Codec identifier - 1 B.</summary>
    public byte Codec;

    /// <summary>Bit flags - 1 B.</summary>
    public byte Flags;

    /// <summary>Scheduling priority - 1 B.</summary>
    public byte Priority;

    /// <summary>Alignment filler, keeps the layout explicit - 1 B.</summary>
    public byte Reserved;

    /// <summary>Bulk frame bytes - 92 B, never parsed by the strategies.</summary>
    public fixed byte Payload[92];
}