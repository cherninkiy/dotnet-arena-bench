using System.Buffers;
using System.Text.Json;

using ArenaBench.Core;

using ArenaBench.Serialization.Core.Sources;
using ArenaBench.Serialization.Core.Text;
using BenchmarkDotNet.Attributes;

namespace ArenaBench.Serialization.Bench;

/// <summary>
/// T3 text/JSON benchmarks (concept.md 5.1, День 2): UTF-8 text parse with
/// SearchValues, Utf8JsonWriter/Utf8JsonReader and the cells where the arena is
/// expected to lose (C03 vs C06 vs C07).
/// </summary>
public class T3TextBench
{
    /// <summary>Lines of the text payload.</summary>
    private const int LineCount = 512;

    /// <summary>Messages of the JSON payload.</summary>
    private const int MessageCount = 64;

    /// <summary>Capacity of the arena buffers - one message needs far less.</summary>
    private const int ArenaCapacity = 16 * 1024;

    /// <summary>Room for one hand-written message.</summary>
    private const int ManualCapacity = 256;

    private byte[] _text = null!;
    private byte[] _json = null!;
    private ArenaPayloadSource _textArena = null!;
    private ArenaPayloadSource _jsonArena = null!;
    private TextMessage _message = null!;

    private ArrayBufferWriter<byte> _arrayBuffer = null!;
    private Utf8JsonWriter _managedWriter = null!;
    private Arena _writerArena = null!;
    private ArenaBufferWriter _arenaWriter = null!;
    private Utf8JsonWriter _arenaWriterJson = null!;
    private Arena _manualArena = null!;

    [GlobalSetup]
    public void Setup()
    {
        _text = TextPayload.Create(LineCount);
        _json = JsonPayload.Create(MessageCount);
        _textArena = ArenaPayloadFactory.Create(_text, alignment: 16);
        _jsonArena = ArenaPayloadFactory.Create(_json, alignment: 16);
        _message = TextMessage.Create(0);

        _arrayBuffer = new ArrayBufferWriter<byte>(ArenaCapacity);
        _managedWriter = new Utf8JsonWriter(_arrayBuffer);

        _writerArena = new Arena((nuint)ArenaCapacity);
        _arenaWriter = new ArenaBufferWriter(_writerArena, ArenaCapacity);
        _arenaWriterJson = new Utf8JsonWriter(_arenaWriter);

        _manualArena = new Arena((nuint)(ManualCapacity + 64));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _managedWriter.Dispose();
        _arenaWriterJson.Dispose();
        _arenaWriter.Dispose();
        _writerArena.Dispose();
        _manualArena.Dispose();
        _textArena.Dispose();
        _jsonArena.Dispose();
    }

    /// <summary>C01: UTF-8 text parse over byte[] with SearchValues + Utf8Parser.</summary>
    [Benchmark(Baseline = true)]
    public long C01_Text_Span_SearchValues() => TextPayload.ParseSpan(_text);

    /// <summary>C02: the same parse over arena memory.</summary>
    [Benchmark]
    public long C02_Text_Arena_SearchValues() => TextPayload.ParseSpan(_textArena.Span);

    /// <summary>C03: Utf8JsonWriter into ArrayBufferWriter - the managed baseline.</summary>
    [Benchmark]
    public long C03_Json_ArrayBufferWriter()
    {
        _arrayBuffer.Clear();
        _managedWriter.Reset(_arrayBuffer);
        JsonPayload.WriteMessage(_managedWriter, _message);
        _managedWriter.Flush();
        return _arrayBuffer.WrittenCount;
    }

    /// <summary>C04: Utf8JsonWriter into the arena-backed IBufferWriter (concept.md 12.2).</summary>
    [Benchmark]
    public long C04_Json_ArenaBufferWriter()
    {
        _arenaWriter.Reset();
        _arenaWriterJson.Reset(_arenaWriter);
        JsonPayload.WriteMessage(_arenaWriterJson, _message);
        _arenaWriterJson.Flush();
        return _arenaWriter.WrittenCount;
    }

    /// <summary>C05: Utf8JsonReader over byte[].</summary>
    [Benchmark]
    public long C05_Json_Utf8JsonReader() => JsonPayload.Parse(_json);

    /// <summary>C06: Utf8JsonReader over arena memory.</summary>
    [Benchmark]
    public long C06_Json_Arena_Utf8JsonReader() => JsonPayload.Parse(_jsonArena.Span);

    /// <summary>C07: JsonSerializer to a string - the obvious API, measured for the "arena loses" cell.</summary>
    [Benchmark]
    public long C07_Json_Serializer_ToString() => JsonSerializer.Serialize(_message).Length;

    /// <summary>C08: JsonSerializer to UTF-8 bytes.</summary>
    [Benchmark]
    public long C08_Json_Serializer_ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(_message).Length;

    /// <summary>C09: the same message written by hand into arena memory (Reset + bump per iteration).</summary>
    [Benchmark]
    public long C09_Text_Manual_ArenaAppend()
    {
        _manualArena.Reset();
        Span<byte> target = _manualArena.AllocSpan<byte>(ManualCapacity);
        return TextPayload.WriteMessageManual(target, _message);
    }

    /// <summary>C10: the managed text path - GetString + Split + double.Parse.</summary>
    [Benchmark]
    public long C10_Text_Managed_StringParse() => TextPayload.ParseManaged(_text);
}