using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace ArenaBench.Serialization.Core.Text;

/// <summary>
/// T3 text payload (concept.md 5.1: text / fixed string). One line per message
/// - <c>name,demand,code</c> - terminated by a newline, ASCII only, so the
/// span-based reader (C01/C02) and the string-based reader (C10) parse the very
/// same bytes and have to agree on the fingerprint.
/// </summary>
public static class TextPayload
{
    /// <summary>Delimiters of the line format: the two commas and the line break.</summary>
    private static readonly SearchValues<byte> Delimiters = SearchValues.Create(",\n"u8);

    /// <summary>Upper bound of a line length, used as the builder's size hint.</summary>
    public const int MaxLineLength = 48;

    /// <summary>Decimal format of the demand field.</summary>
    private static readonly StandardFormat DemandFormat = new('F', 3);

    /// <summary>Builds <paramref name="lineCount"/> deterministic lines.</summary>
    public static byte[] Create(int lineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineCount);

        var buffer = new ArrayBufferWriter<byte>(lineCount * MaxLineLength);
        for (int i = 0; i < lineCount; i++)
        {
            int written = WriteLine(buffer.GetSpan(MaxLineLength), i);
            buffer.Advance(written);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Writes one line and returns its length in bytes.</summary>
    public static int WriteLine(Span<byte> destination, int lineIndex)
    {
        int written = Encoding.ASCII.GetBytes($"flow-{lineIndex}", destination);
        destination[written++] = (byte)',';
        Utf8Formatter.TryFormat(DemandOf(lineIndex), destination[written..], out int demandWritten, DemandFormat);
        written += demandWritten;
        destination[written++] = (byte)',';
        written += Encoding.ASCII.GetBytes(CodeOf(lineIndex), destination[written..]);
        destination[written++] = (byte)'\n';
        return written;
    }

    /// <summary>C01/C02: SearchValues + Utf8Parser - no string, no allocation.</summary>
    public static long ParseSpan(ReadOnlySpan<byte> payload)
    {
        long sum = 0;
        int offset = 0;
        while (offset < payload.Length)
        {
            ReadOnlySpan<byte> rest = payload[offset..];
            int nameEnd = rest.IndexOfAny(Delimiters);
            ReadOnlySpan<byte> afterName = rest[(nameEnd + 1)..];
            int demandEnd = afterName.IndexOfAny(Delimiters);
            ReadOnlySpan<byte> afterDemand = afterName[(demandEnd + 1)..];
            int codeEnd = afterDemand.IndexOfAny(Delimiters);

            Utf8Parser.TryParse(afterName[..demandEnd], out double demand, out _);
            sum += nameEnd + (long)demand + ParseCode(afterDemand[..codeEnd]);
            offset += nameEnd + demandEnd + codeEnd + 3;
        }

        return sum;
    }

    /// <summary>C10: the managed world - one string for the whole payload, then Split and double.Parse.</summary>
    public static long ParseManaged(ReadOnlySpan<byte> payload)
    {
        string text = Encoding.UTF8.GetString(payload);
        long sum = 0;
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(',');
            sum += fields[0].Length
                + (long)double.Parse(fields[1], CultureInfo.InvariantCulture)
                + ParseCode(fields[2].AsSpan());
        }

        return sum;
    }

    /// <summary>Fingerprint of the generated lines: what both parsers have to reproduce.</summary>
    public static long Fingerprint(int lineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineCount);

        long sum = 0;
        for (int i = 0; i < lineCount; i++)
        {
            sum += $"flow-{i}".Length
                + (long)DemandOf(i)
                + int.Parse(CodeOf(i).AsSpan(1), CultureInfo.InvariantCulture);
        }

        return sum;
    }

    /// <summary>
    /// C09: writes one message as JSON by hand - literals plus Utf8Formatter, no
    /// writer object and no escaping. The output is byte-identical to
    /// Utf8JsonWriter for the generated (ASCII, escape-free) messages, which the
    /// tests pin down, so C09 and C03/C04 do the same work.
    /// </summary>
    public static int WriteMessageManual(Span<byte> destination, in TextMessage message)
    {
        int written = Append(destination, 0, "{\"Timestamp\":");
        Utf8Formatter.TryFormat(message.Timestamp, destination[written..], out int count);
        written += count;
        written = Append(destination, written, ",\"FlowId\":");
        Utf8Formatter.TryFormat(message.FlowId, destination[written..], out count);
        written += count;
        written = Append(destination, written, ",\"SourceNode\":");
        Utf8Formatter.TryFormat(message.SourceNode, destination[written..], out count);
        written += count;
        written = Append(destination, written, ",\"TargetNode\":");
        Utf8Formatter.TryFormat(message.TargetNode, destination[written..], out count);
        written += count;
        written = Append(destination, written, ",\"Demand\":");
        Utf8Formatter.TryFormat(message.Demand, destination[written..], out count);
        written += count;
        written = Append(destination, written, ",\"Name\":\"");
        written = Append(destination, written, message.Name);
        written = Append(destination, written, "\",\"Path\":\"");
        written = Append(destination, written, message.Path);
        return Append(destination, written, "\"}");
    }

    private static int Append(Span<byte> destination, int written, string text) =>
        written + Encoding.ASCII.GetBytes(text, destination[written..]);

    private static double DemandOf(int lineIndex) => 1000.0 + lineIndex;

    private static string CodeOf(int lineIndex) => $"A{lineIndex % 100:D2}";

    private static int ParseCode(ReadOnlySpan<byte> code) => ((code[1] - '0') * 10) + (code[2] - '0');

    private static int ParseCode(ReadOnlySpan<char> code) => ((code[1] - '0') * 10) + (code[2] - '0');
}