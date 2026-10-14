using System.Buffers;
using System.Text.Json;

using ArenaBench.Serialization.Core.Sources;
using ArenaBench.Serialization.Core.Text;

namespace ArenaBench.Serialization.Tests;

/// <summary>
/// The T3 payloads: both text parsers and both JSON paths have to agree with the
/// generator, and the hand-written writer has to produce exactly the bytes
/// Utf8JsonWriter produces - otherwise C09 would not be comparable with C03/C04.
/// </summary>
public sealed class TextPayloadTests
{
    private const int LineCount = 128;
    private const int MessageCount = 16;

    [Fact]
    public void TextParsers_AgreeWithTheFingerprint()
    {
        byte[] payload = TextPayload.Create(LineCount);
        long expected = TextPayload.Fingerprint(LineCount);

        Assert.NotEqual(0, expected);
        Assert.Equal(expected, TextPayload.ParseSpan(payload));
        Assert.Equal(expected, TextPayload.ParseManaged(payload));
    }

    [Fact]
    public void JsonReader_AgreesWithTheFingerprint()
    {
        byte[] payload = JsonPayload.Create(MessageCount);
        long expected = JsonPayload.Fingerprint(MessageCount);

        Assert.NotEqual(0, expected);
        Assert.Equal(expected, JsonPayload.Parse(payload));
    }

    [Fact]
    public void JsonReader_ReadsArenaMemory()
    {
        byte[] payload = JsonPayload.Create(MessageCount);
        using ArenaPayloadSource arena = ArenaPayloadFactory.Create(payload, alignment: 64);

        Assert.Equal(JsonPayload.Fingerprint(MessageCount), JsonPayload.Parse(arena.Span));
    }

    [Fact]
    public void SingleMessage_FingerprintSurvivesTheJsonRoundTrip()
    {
        byte[] payload = JsonPayload.Create(1);

        Assert.Equal(TextMessage.Create(0).Fingerprint(), JsonPayload.Parse(payload));
    }

    [Fact]
    public void MalformedTextLine_Throws()
    {
        // No delimiter after the demand field: the strict parser must fail instead
        // of slicing to the end of the payload and looping forever.
        byte[] malformed = "flow-1,1000.000"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => TextPayload.ParseSpan(malformed));
    }

    [Fact]
    public void UnparsableDemandField_Throws()
    {
        byte[] malformed = "flow-1,not-a-number,A01\n"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => TextPayload.ParseSpan(malformed));
    }

    [Fact]
    public void NonArrayJson_Throws()
    {
        byte[] notAnArray = "{\"Timestamp\":1}"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => JsonPayload.Parse(notAnArray));
    }

    [Fact]
    public void JsonPropertyOfTheWrongType_Throws()
    {
        byte[] wrongType = "[{\"Timestamp\":\"abc\"}]"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => JsonPayload.Parse(wrongType));
    }

    [Fact]
    public void ManualWriter_TooSmallBuffer_Throws()
    {
        byte[] tooSmall = new byte[TextPayload.MaxMessageLength - 1];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TextPayload.WriteMessageManual(tooSmall, TextMessage.Create(1)));
    }

    [Fact]
    public void ManualWriter_MatchesUtf8JsonWriter()
    {
        TextMessage message = TextMessage.Create(5);

        var expected = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(expected))
        {
            JsonPayload.WriteMessage(writer, message);
        }

        byte[] manual = new byte[256];
        int written = TextPayload.WriteMessageManual(manual, message);

        Assert.Equal(expected.WrittenSpan.ToArray(), manual.AsSpan(0, written).ToArray());
    }
}