namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// Wire byte order of the T2 image (concept.md 6: native LE, explicit LE,
/// explicit BE). <see cref="LittleEndian"/> matches the native order of every
/// platform the PoC runs on, so a native-order read needs no swap.
/// </summary>
public enum ByteOrder
{
    /// <summary>Little-endian wire image - the order <c>BinaryWriter</c> emits.</summary>
    LittleEndian,

    /// <summary>Big-endian wire image - network byte order.</summary>
    BigEndian,
}