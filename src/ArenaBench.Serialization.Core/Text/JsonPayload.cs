using System.Buffers;
using System.Text.Json;

using ArenaBench.Serialization.Core.Formats;

namespace ArenaBench.Serialization.Core.Text;

/// <summary>
/// T3 JSON payload (concept.md 5.1: JSON). The image is written once with
/// Utf8JsonWriter - the same writer C03/C04 measure - so the readers C05/C06
/// parse exactly the bytes the writers produce, and the fingerprint they have
/// to reproduce comes from <see cref="TextMessage.Create"/>.
/// </summary>
public static class JsonPayload
{
    /// <summary>Writes <paramref name="messageCount"/> messages as a JSON array.</summary>
    public static byte[] Create(int messageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messageCount);

        var buffer = new ArrayBufferWriter<byte>(messageCount * 200);
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartArray();
        for (int i = 0; i < messageCount; i++)
        {
            WriteMessage(writer, TextMessage.Create(i));
        }

        writer.WriteEndArray();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Writes one message; the C03/C04 writers call this too, so every writer sees the same shape.</summary>
    public static void WriteMessage(Utf8JsonWriter writer, TextMessage message)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Timestamp", message.Timestamp);
        writer.WriteNumber("FlowId", message.FlowId);
        writer.WriteNumber("SourceNode", message.SourceNode);
        writer.WriteNumber("TargetNode", message.TargetNode);
        writer.WriteNumber("Demand", message.Demand);
        writer.WriteString("Name", message.Name);
        writer.WriteString("Path", message.Path);
        writer.WriteEndObject();
    }

    /// <summary>
    /// C05/C06: parses the array with Utf8JsonReader, without a single string.
    /// The shape is validated as it is read: the payload must be an array of
    /// objects, and every property must have the type the fingerprint expects, so
    /// a malformed image fails fast instead of leaving a silently wrong number.
    /// </summary>
    public static long Parse(ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new InvalidDataException("A T3 JSON payload must be an array of messages.");
        }

        long sum = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            RequireToken(in reader, JsonTokenType.StartObject, "message");
            sum += ReadMessage(ref reader);
        }

        return sum;
    }

    /// <summary>Fingerprint of the generated messages: what the readers have to reproduce.</summary>
    public static long Fingerprint(int messageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messageCount);

        long sum = 0;
        for (int i = 0; i < messageCount; i++)
        {
            sum += TextMessage.Create(i).Fingerprint();
        }

        return sum;
    }

    /// <summary>
    /// Reads one object and folds it exactly like <see cref="TextMessage.Fingerprint"/>,
    /// so a writer/reader mismatch shows up as a different number.
    /// </summary>
    private static long ReadMessage(ref Utf8JsonReader reader)
    {
        long timestamp = 0;
        int flowId = 0;
        int sourceNode = 0;
        int targetNode = 0;
        int nameLength = 0;
        int pathLength = 0;
        double demand = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            if (reader.ValueTextEquals("Timestamp"))
            {
                reader.Read();
                RequireToken(in reader, JsonTokenType.Number, "Timestamp");
                timestamp = reader.GetInt64();
            }
            else if (reader.ValueTextEquals("FlowId"))
            {
                reader.Read();
                RequireToken(in reader, JsonTokenType.Number, "FlowId");
                flowId = reader.GetInt32();
            }
            else if (reader.ValueTextEquals("SourceNode"))
            {
                reader.Read();
                RequireToken(in reader, JsonTokenType.Number, "SourceNode");
                sourceNode = reader.GetInt32();
            }
            else if (reader.ValueTextEquals("TargetNode"))
            {
                reader.Read();
                RequireToken(in reader, JsonTokenType.Number, "TargetNode");
                targetNode = reader.GetInt32();
            }
            else if (reader.ValueTextEquals("Demand"))
            {
                reader.Read();
                RequireToken(in reader, JsonTokenType.Number, "Demand");
                demand = reader.GetDouble();
            }
            else if (reader.ValueTextEquals("Name"))
            {
                reader.Read();
                RequireToken(in reader, JsonTokenType.String, "Name");
                nameLength = ValueLength(in reader);
            }
            else if (reader.ValueTextEquals("Path"))
            {
                reader.Read();
                RequireToken(in reader, JsonTokenType.String, "Path");
                pathLength = ValueLength(in reader);
            }
        }

        long hash = timestamp;
        hash = RecordFingerprint.Mix(hash, flowId);
        hash = RecordFingerprint.Mix(hash, sourceNode);
        hash = RecordFingerprint.Mix(hash, targetNode);
        hash = RecordFingerprint.Mix(hash, demand);
        hash = RecordFingerprint.Mix(hash, nameLength);
        hash = RecordFingerprint.Mix(hash, pathLength);
        return hash;
    }

    /// <summary>
    /// Length of the raw UTF-8 value. The generators emit ASCII only, so the
    /// byte length equals the string length and no string has to be materialized.
    /// </summary>
    private static int ValueLength(in Utf8JsonReader reader) => (int)reader.ValueSpan.Length;

    private static void RequireToken(in Utf8JsonReader reader, JsonTokenType expected, string what)
    {
        if (reader.TokenType != expected)
        {
            throw new InvalidDataException($"{what} must be {expected}, but the payload has {reader.TokenType}.");
        }
    }
}