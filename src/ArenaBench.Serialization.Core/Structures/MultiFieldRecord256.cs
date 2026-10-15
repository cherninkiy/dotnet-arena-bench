using System.Runtime.InteropServices;

namespace ArenaBench.Serialization.Core.Structures;

/// <summary>
/// T2 multi-field record, 256 B: a 56 B header (14 fields, the widest
/// scalar mix of the ladder) plus a 200 B bulk payload. Four cache lines per
/// record (concept.md, 5.1).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MultiFieldRecord256
{
    /// <summary>Unix time in ticks - 8 B.</summary>
    public long Timestamp;

    /// <summary>Frame sequence number - 8 B.</summary>
    public long Sequence;

    /// <summary>Commodity demand carried by the record - 8 B.</summary>
    public double Demand;

    /// <summary>Commodity cost carried by the record - 8 B.</summary>
    public double Cost;

    /// <summary>Flow identifier - 4 B.</summary>
    public int FlowId;

    /// <summary>Source node of the flow (CSR index) - 4 B.</summary>
    public int SourceNode;

    /// <summary>Target node of the flow (CSR index) - 4 B.</summary>
    public int TargetNode;

    /// <summary>Route identifier - 4 B.</summary>
    public int RouteId;

    /// <summary>Hop count of the encoded path - 2 B.</summary>
    public ushort PathLength;

    /// <summary>MTU of the encoded path - 2 B.</summary>
    public ushort MtuSize;

    /// <summary>Codec identifier - 1 B.</summary>
    public byte Codec;

    /// <summary>Bit flags - 1 B.</summary>
    public byte Flags;

    /// <summary>Scheduling priority - 1 B.</summary>
    public byte Priority;

    /// <summary>Alignment filler, keeps the layout explicit - 1 B.</summary>
    public byte Reserved;

    /// <summary>Bulk frame bytes - 200 B, never parsed by the strategies.</summary>
    public fixed byte Payload[200];
}