using ArenaBench.Serialization.Core.Formats;

namespace ArenaBench.Serialization.Core.Text;

/// <summary>
/// T3 message - the text/JSON payload of PoC-1 (concept.md 5.1). A plain
/// serializable shape: Utf8JsonWriter, JsonSerializer and the text writers all
/// start from these fields, so their numbers describe the same message.
/// </summary>
public sealed record TextMessage
{
    /// <summary>Unix time in ticks.</summary>
    public long Timestamp { get; init; }

    /// <summary>Flow identifier.</summary>
    public int FlowId { get; init; }

    /// <summary>Source node of the flow.</summary>
    public int SourceNode { get; init; }

    /// <summary>Target node of the flow.</summary>
    public int TargetNode { get; init; }

    /// <summary>Commodity demand.</summary>
    public double Demand { get; init; }

    /// <summary>Human readable flow name (ASCII).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Comma separated node path (ASCII).</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Deterministic message for a message index.</summary>
    public static TextMessage Create(int messageIndex) => new()
    {
        Timestamp = 1_700_000_000_000_000_000L + messageIndex,
        FlowId = 10_000 + messageIndex,
        SourceNode = messageIndex,
        TargetNode = messageIndex + 1,
        Demand = 1000.0 + messageIndex,
        Name = $"flow-{messageIndex}",
        Path = "1,2,3,4",
    };

    /// <summary>
    /// Fingerprint of one message: writers and readers of T3 have to agree on
    /// it, and it only mixes values both worlds can produce (string lengths
    /// instead of whole strings, so no reader is forced to allocate).
    /// </summary>
    public long Fingerprint()
    {
        long hash = Timestamp;
        hash = RecordFingerprint.Mix(hash, FlowId);
        hash = RecordFingerprint.Mix(hash, SourceNode);
        hash = RecordFingerprint.Mix(hash, TargetNode);
        hash = RecordFingerprint.Mix(hash, Demand);
        hash = RecordFingerprint.Mix(hash, Name.Length);
        hash = RecordFingerprint.Mix(hash, Path.Length);
        return hash;
    }
}