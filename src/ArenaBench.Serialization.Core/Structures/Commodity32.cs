using System.Runtime.InteropServices;

namespace ArenaBench.Serialization.Core.Structures;

/// <summary>
/// T2 commodity record, 32 B. All fields are scalars - the record exercises
/// the double/float market of the size axis (concept.md, 5.1) and packs
/// without padding.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Commodity32
{
    /// <summary>Unix time in ticks - 8 B.</summary>
    public long Timestamp;

    /// <summary>Commodity demand - 8 B.</summary>
    public double Demand;

    /// <summary>Commodity identifier - 4 B.</summary>
    public int CommodityId;

    /// <summary>Source node of the demand (CSR index) - 4 B.</summary>
    public int SourceNode;

    /// <summary>Target node of the demand (CSR index) - 4 B.</summary>
    public int TargetNode;

    /// <summary>Hop count of the encoded path - 2 B.</summary>
    public ushort PathLength;

    /// <summary>Alignment filler, keeps the layout explicit - 2 B.</summary>
    public ushort Reserved;
}