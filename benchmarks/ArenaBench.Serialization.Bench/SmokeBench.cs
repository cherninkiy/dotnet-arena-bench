using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace ArenaBench.Serialization.Bench;

/// <summary>
/// Day-0 smoke benchmark: verifies the BenchmarkDotNet setup.
/// Replaced by the S-series on Day 1 (concept.md, День 1–2).
/// </summary>
public class SmokeBench
{
    private readonly byte[] _buffer = CreateBuffer();

    private static byte[] CreateBuffer()
    {
        var buffer = new byte[64 * 1024];
        new Random(42).NextBytes(buffer); // fixed seed, concept.md 7.4
        return buffer;
    }

    [Benchmark(Baseline = true)]
    public long BinaryReader_ReadInt32()
    {
        using var reader = new BinaryReader(new MemoryStream(_buffer));
        long sum = 0;
        int count = _buffer.Length / sizeof(int);
        for (int i = 0; i < count; i++)
        {
            sum += reader.ReadInt32();
        }

        return sum;
    }

    [Benchmark]
    public long Span_CastToInt32()
    {
        long sum = 0;
        foreach (int value in MemoryMarshal.Cast<byte, int>(_buffer))
        {
            sum += value;
        }

        return sum;
    }
}
