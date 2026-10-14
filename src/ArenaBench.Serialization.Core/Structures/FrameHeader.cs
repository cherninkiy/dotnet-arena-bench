using System.Runtime.InteropServices;

namespace ArenaBench.Serialization.Core.Structures;

/// <summary>
/// T1 frame header, 8 B (concept.md 5.1: binary stream). Sequential layout
/// with naturally aligned fields and no padding, so the header can be read
/// field by field or as a struct view.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FrameHeader
{
    /// <summary>Payload length in bytes - 4 B.</summary>
    public int PayloadLength;

    /// <summary>Frame kind - 2 B.</summary>
    public ushort Kind;

    /// <summary>Codec identifier - 1 B.</summary>
    public byte Codec;

    /// <summary>Bit flags - 1 B.</summary>
    public byte Flags;
}