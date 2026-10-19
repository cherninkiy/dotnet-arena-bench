using System.Text;

using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Allocator.Core.Workload;

/// <summary>
/// Generates the mixed workload of the M-benchmarks (concept.md 5.2): a schedule
/// of <see cref="CommodityType"/> items plus the payload each type writes. The
/// payloads come from the PoC-1 formats - a T1 frame, the 32 B service record, a
/// UTF-8 message - so the mixed case runs on the bytes the serialization PoC
/// measured (concept.md 5.3: PoC-2 reuses those structures rather than
/// inventing its own).
/// </summary>
public static class WorkloadGenerator
{
    /// <summary>Items the M-benchmarks process per operation.</summary>
    public const int DefaultItemCount = 65536;

    /// <summary>Seed of the schedule - fixed, like every other generator of the PoC (concept.md 7.4).</summary>
    public const int DefaultSeed = 42;

    /// <summary>UTF-8 message the text commodity carries.</summary>
    private const string TextMessage = "commodity=0123456789;capacity=9999;cost=12.34;";

    /// <summary>
    /// Builds a workload of <paramref name="itemCount"/> items where
    /// <paramref name="textShare"/> of them are text and the rest split evenly
    /// between voice and service. The schedule is drawn from a seeded generator,
    /// so it is reproducible and the types interleave instead of arriving in
    /// blocks - a per-type arena only pays off if the types really do mix.
    /// </summary>
    public static MixedWorkload Create(
        int itemCount = DefaultItemCount,
        double textShare = MixedWorkload.BalancedTextShare,
        int seed = DefaultSeed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(itemCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(textShare);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(textShare, 1.0);

        byte[] voice = FrameFormat.CreatePayload(1, ByteOrder.LittleEndian);
        byte[] service = T2Payload.Create<Commodity32Format, Commodity32>(1, ByteOrder.LittleEndian);
        byte[] text = Encoding.UTF8.GetBytes(TextMessage);

        var random = new Random(seed);
        var schedule = new CommodityType[itemCount];
        double voiceShare = (1.0 - textShare) / 2.0;

        for (int index = 0; index < itemCount; index++)
        {
            double draw = random.NextDouble();
            schedule[index] = draw < textShare ? CommodityType.Text
                : draw < textShare + voiceShare ? CommodityType.Voice
                : CommodityType.Service;
        }

        return new MixedWorkload(schedule, voice, service, text, textShare);
    }
}

/// <summary>
/// One generated workload: the schedule and the three payload templates. Item
/// sizes are fixed per type on purpose - a variable per-item size would turn the
/// M-series into a fragmentation benchmark, and the question here is whether
/// separating the types (or not) matters.
/// </summary>
public sealed class MixedWorkload
{
    /// <summary>Share of the balanced case: text is a fifth of the work.</summary>
    public const double BalancedTextShare = 0.2;

    /// <summary>Share of the skewed case: text dominates and crowds the other types out.</summary>
    public const double SkewedTextShare = 0.8;

    /// <summary>Room for the arena's own alignment padding, on top of the exact byte counts.</summary>
    private const nuint AlignmentSlack = 256;

    private readonly CommodityType[] _schedule;
    private readonly byte[] _voice;
    private readonly byte[] _service;
    private readonly byte[] _text;

    internal MixedWorkload(CommodityType[] schedule, byte[] voice, byte[] service, byte[] text, double textShare)
    {
        _schedule = schedule;
        _voice = voice;
        _service = service;
        _text = text;
        TextShare = textShare;
    }

    /// <summary>The schedule: which type each item belongs to, in processing order.</summary>
    public ReadOnlySpan<CommodityType> Schedule => _schedule;

    /// <summary>Items in the schedule.</summary>
    public int ItemCount => _schedule.Length;

    /// <summary>Share of the items the text type takes - the skew axis of M01-M04.</summary>
    public double TextShare { get; }

    /// <summary>Payload one item of <paramref name="type"/> writes, in bytes.</summary>
    public ReadOnlySpan<byte> Template(CommodityType type) => type switch
    {
        CommodityType.Voice => _voice,
        CommodityType.Service => _service,
        _ => _text,
    };

    /// <summary>Items of one type in the schedule.</summary>
    public int CountOf(CommodityType type)
    {
        int count = 0;
        foreach (CommodityType item in _schedule)
        {
            if (item == type)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Bytes an arena needs for one type: every allocation is aligned to 16 B
    /// (ADR-001 D3), so the worst case is one padding block per item.
    /// </summary>
    public nuint ArenaBytesFor(CommodityType type)
        => Padded(CountOf(type), Template(type).Length) + AlignmentSlack;

    /// <summary>Bytes one arena needs for the whole schedule, whatever the mix of types.</summary>
    public nuint TotalArenaBytes()
    {
        nuint total = 0;
        foreach (CommodityType type in Enum.GetValues<CommodityType>())
        {
            total += Padded(CountOf(type), Template(type).Length);
        }

        return total + AlignmentSlack;
    }

    private static nuint Padded(int count, int itemBytes)
        => (nuint)count * (nuint)(((itemBytes + 15) / 16) * 16);
}
