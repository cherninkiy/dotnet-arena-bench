namespace ArenaBench.Serialization.Core.Sources;

/// <summary>
/// A PoC-1 data source (concept.md 5.1) presented as a <see cref="Stream"/>
/// plus the ability to re-arm it. One measured iteration reads the whole
/// payload, so the source has to be rewound (or re-sent, for the loopback
/// network source) before the next one; the rewind is part of the measured
/// body and behaves the same for every source.
/// </summary>
public interface IStreamSource : IDisposable
{
    /// <summary>The stream a record is read from.</summary>
    Stream Stream { get; }

    /// <summary>Byte length of one payload pass.</summary>
    long Length { get; }

    /// <summary>Makes the next read see the payload again, starting at its first byte.</summary>
    void Rewind();
}