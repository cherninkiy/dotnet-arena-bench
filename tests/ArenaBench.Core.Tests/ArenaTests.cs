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
    public void Scope_AfterReset_ThrowsOnDisposeAndStaysArmed()
    {
        using var arena = new Arena(1024);
        ArenaScope scope = arena.Scope();
        _ = arena.AllocBytes(64, 16);
        arena.Reset();
        _ = arena.AllocBytes(256, 16);

        // A ref struct cannot be captured by Assert.Throws, so the disposals are
        // spelled out. The first one must throw; the scope must then stay armed,
        // otherwise the second Dispose would silently swallow the stale mark.
        Assert.True(DisposeExpectingFailure(ref scope), "a stale scope must not roll back silently");
        Assert.True(DisposeExpectingFailure(ref scope), "the scope must stay armed after a failed release");
    }

    private static bool DisposeExpectingFailure(ref ArenaScope scope)
    {
        try
        {
            scope.Dispose();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
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

    [Fact]
    public void CompositeType_UsesDefaultAlignAsItsContract()
    {
        // The BCL exposes no AlignOf<T> for composite unmanaged types
        // (dotnet/runtime #2167), so defaultAlign IS their alignment contract: it has
        // to cover the natural alignment the caller knows about, and this test pins
        // the documented behavior down (whatever defaultAlign asks for is what the
        // pointer gets). Primitives ignore a smaller defaultAlign - see
        // PrimitiveAlign_WinsOverSmallerDefaultAlign.
        using var arena16 = new Arena(1024, defaultAlign: 16);
        using var arena64 = new Arena(1024, defaultAlign: 64);

        Assert.Equal(0, (long)arena16.AllocPtr<Commodity32>(1) % 16);
        Assert.Equal(0, (long)arena64.AllocPtr<Commodity32>(1) % 64);
    }

    [Fact]
    public void Release_CurrentMark_IsANoOp()
    {
        // Boundary case: a mark equal to the current offset restores the same offset,
        // so it is legal and does nothing.
        using var arena = new Arena(1024);
        _ = arena.AllocBytes(64, 16);
        ArenaMark current = arena.Mark();

        arena.Release(current);

        Assert.Equal(64u, (uint)arena.UsedBytes);
    }

    [Fact]
    public void Release_NewerMark_Throws()
    {
        // Out-of-order release (ADR-001 D4): after rolling back to an older mark, the
        // newer one is ahead of the arena state and must be rejected.
        using var arena = new Arena(1024);
        _ = arena.AllocBytes(64, 16);
        ArenaMark earlier = arena.Mark();
        _ = arena.AllocBytes(64, 16);
        ArenaMark newer = arena.Mark();

        arena.Release(earlier);
        Assert.Equal(64u, (uint)arena.UsedBytes);

        Assert.Throws<ArgumentException>(() => arena.Release(newer));
    }

    [Fact]
    public void Release_AfterReset_Throws()
    {
        // ArenaScope documents exactly this: a Reset between the mark and the release
        // invalidates the mark, so the rollback fails instead of restoring an offset
        // the arena no longer has. Here the arena is still empty after the reset, so
        // the generation check is what rejects the mark.
        using var arena = new Arena(1024);
        _ = arena.AllocBytes(64, 16);
        ArenaMark mark = arena.Mark();
        _ = arena.AllocBytes(64, 16);
        arena.Reset();

        Assert.Throws<ArgumentException>(() => arena.Release(mark));
        Assert.Equal(0u, (uint)arena.UsedBytes);
    }

    [Fact]
    public void Release_MarkTakenBeforeReset_AfterGrowingPastIt_Throws()
    {
        // The dangerous shape of the same bug: after the reset the arena grew past
        // the mark again, so an offset comparison alone would accept it and move the
        // offset back over memory that has already been reissued (ADR-001 D9).
        using var arena = new Arena(1024);
        _ = arena.AllocBytes(100, 16);
        ArenaMark mark = arena.Mark();
        arena.Reset();
        _ = arena.AllocBytes(256, 16);

        Assert.Throws<ArgumentException>(() => arena.Release(mark));
        Assert.Equal(256u, (uint)arena.UsedBytes);
    }

    [Fact]
    public void Release_MarkFromAnotherArena_Throws()
    {
        // Both arenas start at generation 0, so without the arena identity a foreign
        // mark looks valid and would roll back the wrong block.
        using var first = new Arena(1024);
        using var second = new Arena(1024);
        _ = second.AllocBytes(64, 16);
        ArenaMark foreign = second.Mark();
        _ = first.AllocBytes(128, 16);

        Assert.Throws<ArgumentException>(() => first.Release(foreign));
        Assert.Equal(128u, (uint)first.UsedBytes);
    }

    [Fact]
    public void MemoryView_AfterReset_Throws()
    {
        // ADR-001 D9 with the generation check: the view is not silently stale, it
        // fails loudly instead of reading memory that has been reissued.
        using var arena = new Arena(1024);
        Memory<int> view = arena.AllocMemory<int>(16);
        view.Span[0] = 42; // valid before the reset

        arena.Reset();

        Assert.Throws<InvalidOperationException>(() => _ = view.Span);
        Assert.Throws<InvalidOperationException>(() => view.Pin());
    }

    [Fact]
    public void MemoryView_AfterDispose_Throws()
    {
        var arena = new Arena(1024);
        Memory<int> view = arena.AllocMemory<int>(16);
        arena.Dispose();

        Assert.Throws<InvalidOperationException>(() => _ = view.Span);
    }

    [Fact]
    public void MemoryView_WithoutOwner_CannotDetectStaleness()
    {
        // A view created from a bare pointer has nothing to compare against, so the
        // old documented behavior (undefined behavior) stays for that case.
        using var arena = new Arena(1024);
        var manager = new ArenaMemoryManager<int>(arena.AllocPtr<int>(16), 16);

        arena.Reset();

        Assert.Equal(16, manager.GetSpan().Length);
    }

    [Fact]
    public void ScopedReuse_KeepsTheGeneration()
    {
        // Mark/Release only rolls the offset back: the memory is not reissued, so a
        // view that shares the arena with a scope stays valid. Only Reset/Dispose bump
        // the generation.
        using var arena = new Arena(1024);
        Memory<int> view = arena.AllocMemory<int>(16);
        uint generation = arena.Generation;

        ArenaMark mark = arena.Mark();
        _ = arena.AllocBytes(64, 16);
        arena.Release(mark);

        Assert.Equal(generation, arena.Generation);
        Assert.Equal(16, view.Span.Length);
    }
}
