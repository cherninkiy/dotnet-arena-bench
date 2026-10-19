using ArenaBench.Allocator.Core.Workload;
using ArenaBench.Core;
using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Allocator.Tests;

/// <summary>
/// The mixed workload feeds M01-M04, so the tests fix what the benches rely on:
/// the schedule is reproducible, the shares really are the requested ones, the
/// payload templates are the PoC-1 shapes, and the arena sizes the workload
/// reports are enough for the items it will allocate.
/// </summary>
public class WorkloadTests
{
    [Fact]
    public void Create_IsDeterministicForOneSeed()
    {
        MixedWorkload first = WorkloadGenerator.Create(256, MixedWorkload.BalancedTextShare);
        MixedWorkload second = WorkloadGenerator.Create(256, MixedWorkload.BalancedTextShare);

        Assert.Equal(first.Schedule.ToArray(), second.Schedule.ToArray());
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public void Create_MatchesTheRequestedShare(double textShare)
    {
        const int items = 20000;
        MixedWorkload workload = WorkloadGenerator.Create(items, textShare);

        int text = workload.CountOf(CommodityType.Text);
        int voice = workload.CountOf(CommodityType.Voice);
        int service = workload.CountOf(CommodityType.Service);

        Assert.Equal(items, text + voice + service);
        Assert.InRange(text / (double)items, textShare - 0.02, textShare + 0.02);

        // The rest of the work is split evenly between the two binary types.
        double binaryShare = (1.0 - textShare) / 2.0;
        Assert.InRange(voice / (double)items, binaryShare - 0.02, binaryShare + 0.02);
        Assert.InRange(service / (double)items, binaryShare - 0.02, binaryShare + 0.02);
    }

    [Fact]
    public void Create_RejectsUnreasonableInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkloadGenerator.Create(itemCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkloadGenerator.Create(textShare: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkloadGenerator.Create(textShare: 1.1));
    }

    [Fact]
    public void Template_CarriesThePoc1Shapes()
    {
        MixedWorkload workload = WorkloadGenerator.Create(64);

        // Voice is one T1 frame, service is the 32 B record, text is a message.
        Assert.Equal(FrameFormat.FrameSize(0), workload.Template(CommodityType.Voice).Length);
        Assert.Equal(Commodity32Format.Size, workload.Template(CommodityType.Service).Length);
        Assert.True(workload.Template(CommodityType.Text).Length > 0);
    }

    [Fact]
    public void ArenaBytesFor_FitsEveryItemOfTheType()
    {
        MixedWorkload workload = WorkloadGenerator.Create(512, MixedWorkload.SkewedTextShare);

        foreach (CommodityType type in Enum.GetValues<CommodityType>())
        {
            int count = workload.CountOf(type);
            int itemBytes = workload.Template(type).Length;
            using var arena = new Arena(workload.ArenaBytesFor(type));

            for (int item = 0; item < count; item++)
            {
                Span<byte> slot = arena.AllocSpan<byte>(itemBytes);
                slot[itemBytes - 1] = 0x01;
            }

            Assert.True(arena.UsedBytes >= (nuint)(count * itemBytes));
        }
    }

    [Fact]
    public void TotalArenaBytes_FitsTheWholeSchedule()
    {
        MixedWorkload workload = WorkloadGenerator.Create(512);
        using var arena = new Arena(workload.TotalArenaBytes());

        for (int index = 0; index < workload.ItemCount; index++)
        {
            CommodityType type = workload.Schedule[index];
            Span<byte> slot = arena.AllocSpan<byte>(workload.Template(type).Length);
            slot[0] = (byte)index;
        }

        Assert.True(arena.UsedBytes <= arena.Size);
    }

    [Fact]
    public void Schedule_InterleavesTheTypes()
    {
        MixedWorkload workload = WorkloadGenerator.Create(256);

        // A per-type arena only pays off if the types really do mix: all three
        // have to show up inside the first few dozen items.
        bool[] seen = new bool[3];
        for (int index = 0; index < 64; index++)
        {
            seen[(int)workload.Schedule[index]] = true;
        }

        Assert.All(seen, value => Assert.True(value));
    }
}
