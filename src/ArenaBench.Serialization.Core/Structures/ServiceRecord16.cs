using System.Runtime.InteropServices;

namespace ArenaBench.Serialization.Core.Structures;

/// <summary>
/// T2 fixed-layout service record, 16 B (concept.md, 5.1). Sequential
/// layout with naturally aligned fields: the wire image carries no implicit
/// padding, so the same bytes can be read field-by-field with
/// <c>BinaryReader</c> / <c>BinaryPrimitives</c> or as a struct view.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ServiceRecord16
{
    /// <summary>Unix time in ticks - 8 B.</summary>
    public long Timestamp;

    /// <summary>Service identifier - 4 B.</summary>
    public int ServiceId;

    /// <summary>UDP/TCP port - 2 B.</summary>
    public ushort Port;

    /// <summary>Codec identifier - 1 B.</summary>
    public byte Codec;

    /// <summary>Bit flags - 1 B.</summary>
    public byte Flags;
}