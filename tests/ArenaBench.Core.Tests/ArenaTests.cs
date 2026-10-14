using System.Buffers;
using System.Runtime.InteropServices;
using ArenaBench.Core;
using Xunit;

namespace ArenaBench.Core.Tests;

// Shares the "Arena" collection with the leak tests: they observe the global
// SafeArenaHandle.LiveCount, so arena-creating tests must stay serialized.
[Collection("Arena")]
public class ArenaTests
{
#pragma warning disable CS0649 // test payload struct, fields are never assigned by design
    private struct Commodity32 // 32 bytes, natural alignment 4 — like T2 records
    {
        public int A, B, C, D, E, F, G, H;
    }
#pragma warning restore CS0649

    [Fact]
    public void MarkRelease_RestoresOffsetExactly()
    {
        using var arena = new Arena(1024);
        ArenaMark mark = arena.Mark();
        _ = arena.AllocSpan<Commodity32>(4);
        Assert.Equal(128u, (uint)arena.UsedBytes);
        arena.Release(mark);
        Assert.Equal(0u, (uint)arena.UsedBytes);
    }

    [Fact]
    public void Scope_DisposeRollsBackToMark()
    {
        using var arena = new Arena(1024);
        using (var scope = arena.Scope())
        {
            _ = arena.AllocBytes(64, 16);
            Assert.Equal(64u, (uint)arena.UsedBytes);
        }

        Assert.Equal(0u, (uint)arena.UsedBytes);
    }

    [Fact]
    public void SequentialMarkRelease_Works()
    {
        using var arena = new Arena(1024);
        ArenaMark first = arena.Mark();
        _ = arena.AllocBytes(16, 16);
        ArenaMark second = arena.Mark();
        _ = arena.AllocBytes(16, 16);

        arena.Release(second);
        Assert.Equal((uint)second.Offset, (uint)arena.UsedBytes);
        arena.Release(first);
        Assert.Equal(0u, (uint)arena.UsedBytes);
    }

    [Fact]
    public void Reset_ReturnsOffsetToZeroAndAllowsReuse()
    {
        using var arena = new Arena(1024);
        Span<byte> first = arena.AllocSpan<byte>(512);
        first[0] = 42;
        arena.Reset();
        Assert.Equal(0u, (uint)arena.UsedBytes);

        Span<byte> second = arena.AllocSpan<byte>(512);
        second[0] = 7;
        Assert.Equal(512u, (uint)arena.UsedBytes);
        Assert.Equal(7, second[0]);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void DefaultAlign_AutoAllocationsAreAligned(int defaultAlign)
    {
        using var arena = new Arena(16384, defaultAlign);
        for (int i = 0; i < 100; i++)
        {
            IntPtr pointer = arena.AllocPtr<Commodity32>(1);
            Assert.Equal(0, (long)pointer % defaultAlign);
        }
    }

    [Fact]
    public void ExplicitAlign_AllocBytesAreAlignedTo64()
    {
        using var arena = new Arena(16384);
        for (int i = 0; i < 100; i++)
        {
            IntPtr pointer = arena.AllocPtr(5, 64);
            Assert.Equal(0, (long)pointer % 64);
        }
    }

    [Fact]
    public void OwnedArena_Has64ByteAlignedBase()
    {
        using var arena = new Arena(1024);
        Assert.Equal(0, (long)arena.AllocPtr(1, 64) % 64); // first alloc sits at the base
    }

    [Fact]
    public void PrimitiveAlign_WinsOverSmallerDefaultAlign()
    {
        using var arena = new Arena(1024, defaultAlign: 4);
        IntPtr pointer = arena.AllocPtr<long>(1);
        Assert.Equal(0, (long)pointer % 8); // natural alignment of long beats defaultAlign 4
    }

    [Fact]
    public void ImpossibleSize_ThrowsOutOfMemoryException()
    {
        nuint beyondAnyAddressSpace = (nuint)1024 << 40; // 1 PiB
        Assert.Throws<OutOfMemoryException>(() => new Arena(beyondAnyAddressSpace));
    }

    [Fact]
    public void Overflow_ThrowsInvalidOperationException()
    {
        using var arena = new Arena(64);
        Assert.Throws<InvalidOperationException>(() => arena.AllocBytes(65, 16));
    }

    [Fact]
    public void AllocSpanZeroed_ReturnsZerosOverPoisonedMemory()
    {
        using var arena = new Arena(1024);
        arena.AllocBytes(1024, 16).Fill(0xFF);
        arena.Reset();

        Span<int> span = arena.AllocSpanZeroed<int>(64);
        foreach (int value in span)
        {
            Assert.Equal(0, value);
        }
    }

    [Fact]
    public void ExternalMemory_IsNotFreedByArena()
    {
        IntPtr block = Marshal.AllocHGlobal(256);
        try
        {
            {
                using var arena = new Arena(block, 256, ownsMemory: false);
                Span<byte> span = arena.AllocSpan<byte>(256);
                span[0] = 0xAB;
            }

            // Arena is disposed; the external block must remain valid and readable.
            Assert.Equal(0xAB, Marshal.ReadByte(block, 0));
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
    }

    [Fact]
    public void AllocMemory_RoundTrip()
    {
        using var arena = new Arena(1024);
        Memory<int> memory = arena.AllocMemory<int>(16);
        memory.Span[0] = 11;
        memory.Span[15] = 15;
        Assert.Equal(11, memory.Span[0]);
        Assert.Equal(15, memory.Span[15]);
    }

    [Fact]
    public unsafe void AllocMemory_Pinned_ReturnsSamePointer()
    {
        using var arena = new Arena(1024);
        IntPtr expected = arena.AllocPtr<int>(16);
        arena.Reset(); // next allocation lands at the same aligned offset

        Memory<int> memory = arena.AllocMemory<int>(16);
        using MemoryHandle handle = memory.Pin();
        Assert.Equal(expected, (nint)handle.Pointer); // unmanaged memory: always the base
    }

    [Fact]
    public unsafe void Pin_OutOfRangeIndex_Throws()
    {
        using var arena = new Arena(1024);
        IntPtr pointer = arena.AllocPtr<int>(16);
        var manager = new ArenaMemoryManager<int>(pointer, 16);

        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Pin(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Pin(17));

        using MemoryHandle onePastEnd = manager.Pin(16); // legal one-past-end
        Assert.Equal(pointer + 16 * sizeof(int), (nint)onePastEnd.Pointer);
    }

    [Fact]
    public void ZeroCount_ReturnsEmptyViewsOverAValidPointer()
    {
        using var arena = new Arena(1024);

        Span<int> span = arena.AllocSpan<int>(0);
        Memory<int> memory = arena.AllocMemory<int>(0);
        IntPtr pointer = arena.AllocPtr<int>(0);

        Assert.True(span.IsEmpty);
        Assert.Equal(0, memory.Length);
        Assert.True(memory.Span.IsEmpty);
        Assert.NotEqual(IntPtr.Zero, pointer);
    }

    [Fact]
    public void ZeroCount_StillSpendsAlignmentPadding()
    {
        using var arena = new Arena(1024);
        arena.AllocBytes(1, 1);
        nuint afterByte = arena.UsedBytes;

        arena.AllocPtr<int>(0); // no elements, but the offset is aligned to 16

        Assert.Equal(1L, (long)afterByte);
        Assert.Equal(16L, (long)arena.UsedBytes);
    }

    [Fact]
    public unsafe void ZeroCount_ManagerPinStaysValid()
    {
        using var arena = new Arena(1024);
        IntPtr pointer = arena.AllocPtr<int>(0);
        var manager = new ArenaMemoryManager<int>(pointer, 0);

        Assert.True(manager.GetSpan().IsEmpty);

        using MemoryHandle handle = manager.Pin(0); // one-past-end of an empty view
        Assert.Equal((nint)pointer, (nint)handle.Pointer);
    }
}
