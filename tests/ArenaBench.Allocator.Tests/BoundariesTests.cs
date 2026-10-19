using ArenaBench.Allocator.Core.Boundaries;
using ArenaBench.Core;

namespace ArenaBench.Allocator.Tests;

/// <summary>
/// The boundary scenarios hand pointers to code we do not control, so the tests
/// pin the contract down: libc really touches our memory, the raw export
/// resolves and returns what the LibraryImport wrapper returns, the callback is
/// reachable through the unmanaged ABI, and an arena can run over a mapping it
/// does not own.
/// </summary>
public class BoundariesTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(64)]
    [InlineData(1024)]
    public unsafe void NativeReadsOurs_CopiesIntoTheSink(int size)
    {
        using var arena = new Arena((nuint)(size * 2) + 64);
        IntPtr ours = arena.AllocPtr<byte>(size);
        IntPtr sink = arena.AllocPtr<byte>(size);
        Span<byte> oursSpan = new((void*)ours, size);
        Span<byte> sinkSpan = new((void*)sink, size);

        for (int index = 0; index < size; index++)
        {
            oursSpan[index] = (byte)(index + 1);
        }

        Assert.Equal(size, PInvokeScenarios.NativeReadsOurs(ours, sink, size));
        Assert.Equal(oursSpan.ToArray(), sinkSpan.ToArray());
    }

    [Fact]
    public unsafe void NativeReadsOursRaw_MatchesTheWrapper()
    {
        if (!PInvokeScenarios.RawExportsAvailable)
        {
            // The raw path is best-effort: on a platform where the C library or a
            // symbol does not resolve there is no left-hand side to compare with,
            // and the wrapper path above is what the boundary depends on.
            return;
        }

        const int size = 64;
        using var arena = new Arena(256);
        IntPtr ours = arena.AllocPtr<byte>(size);
        IntPtr viaWrapper = arena.AllocPtr<byte>(size);
        IntPtr viaRaw = arena.AllocPtr<byte>(size);
        Span<byte> oursSpan = new((void*)ours, size);
        for (int index = 0; index < size; index++)
        {
            oursSpan[index] = (byte)(index * 3);
        }

        _ = PInvokeScenarios.NativeReadsOurs(ours, viaWrapper, size);
        _ = PInvokeScenarios.NativeReadsOursRaw(ours, viaRaw, size);

        Assert.Equal(
            new Span<byte>((void*)viaWrapper, size).ToArray(),
            new Span<byte>((void*)viaRaw, size).ToArray());
    }

    [Fact]
    public unsafe void NativeWritesOurs_FillsWithThePattern()
    {
        const int size = 128;
        using var arena = new Arena(512);
        IntPtr ours = arena.AllocPtr<byte>(size);
        Span<byte> span = new((void*)ours, size);
        span.Clear();

        _ = PInvokeScenarios.NativeWritesOurs(ours, size);

        Assert.All(span.ToArray(), value => Assert.Equal(0x5A, value));
    }

    [Fact]
    public unsafe void NativeWritesOursRaw_FillsWithThePattern()
    {
        if (!PInvokeScenarios.RawExportsAvailable)
        {
            // See NativeReadsOursRaw_MatchesTheWrapper: the raw path is optional.
            return;
        }

        const int size = 128;
        using var arena = new Arena(512);
        IntPtr ours = arena.AllocPtr<byte>(size);
        Span<byte> span = new((void*)ours, size);
        span.Clear();

        _ = PInvokeScenarios.NativeWritesOursRaw(ours, size);

        Assert.All(span.ToArray(), value => Assert.Equal(0x5A, value));
    }

    [Fact]
    public unsafe void InOut_ReportsEqualBuffers()
    {
        const int size = 32;
        using var arena = new Arena(256);
        IntPtr ours = arena.AllocPtr<byte>(size);
        IntPtr other = arena.AllocPtr<byte>(size);
        Span<byte> oursSpan = new((void*)ours, size);
        for (int index = 0; index < size; index++)
        {
            oursSpan[index] = (byte)(0x40 + index);
        }

        Assert.Equal(0, PInvokeScenarios.InOut(ours, other, size));
        Assert.Equal(oursSpan.ToArray(), new Span<byte>((void*)other, size).ToArray());
    }

    [Fact]
    public unsafe void VerifyEgress_DetectsADifference()
    {
        const int size = 16;
        using var arena = new Arena(256);
        IntPtr payload = arena.AllocPtr<byte>(size);
        IntPtr connection = arena.AllocPtr<byte>(size);
        Span<byte> payloadSpan = new((void*)payload, size);
        payloadSpan.Fill(0x11);

        _ = PInvokeScenarios.Egress(payload, connection, size);
        Assert.Equal(0, PInvokeScenarios.VerifyEgress(payload, connection, size));

        new Span<byte>((void*)connection, size)[0] = 0x7F;
        Assert.NotEqual(0, PInvokeScenarios.VerifyEgress(payload, connection, size));
    }

    [Fact]
    public unsafe void Callback_ChecksumMatchesTheManagedSum()
    {
        const int size = 64;
        using var arena = new Arena(256);
        IntPtr data = arena.AllocPtr<byte>(size);
        Span<byte> span = new((void*)data, size);
        int expected = 0;
        for (int index = 0; index < size; index++)
        {
            span[index] = (byte)index;
            expected += index;
        }

        Assert.Equal(expected, NativeCallbackScenarios.InvokeOnce(data, size));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(64)]
    public unsafe void Callback_RepeatedCallsSumTheSameTotal(int calls)
    {
        const int size = 128;
        using var arena = new Arena(512);
        IntPtr data = arena.AllocPtr<byte>(size);
        new Span<byte>((void*)data, size).Fill(0x03);

        Assert.Equal((long)0x03 * size, NativeCallbackScenarios.InvokeRepeatedly(data, size, calls));
    }

    [Fact]
    public unsafe void Callback_FindTerminatorReturnsTheFirstZero()
    {
        const int size = 32;
        using var arena = new Arena(128);
        IntPtr data = arena.AllocPtr<byte>(size);
        Span<byte> span = new((void*)data, size);
        span.Fill(0x01);

        Assert.Equal(-1, NativeCallbackScenarios.FindTerminator(data, size));

        span[7] = 0;
        Assert.Equal(7, NativeCallbackScenarios.FindTerminator(data, size));
    }

    [Fact]
    public void MappedMemory_ArenaWritesLandInTheView()
    {
        const int size = 4096;
        using var mapped = new MappedMemory(size);
        using var arena = mapped.CreateArena();

        Span<byte> block = arena.AllocSpan<byte>(size);
        block.Fill(0x2A);

        // Read through the BCL view: the arena really wrote the mapped memory.
        Assert.Equal(0x2A, mapped.Accessor.ReadByte(0));
        Assert.Equal(0x2A, mapped.Accessor.ReadByte(size - 1));
    }

    [Fact]
    public void MappedMemory_IsOwnedByTheMappingNotTheArena()
    {
        const int size = 1024;
        MappedMemory mapped = new(size);
        try
        {
            Arena arena = mapped.CreateArena();
            arena.AllocSpan<byte>(size).Fill(0x5C);
            arena.Dispose(); // the arena must not free memory it does not own

            Assert.Equal(0x5C, mapped.Accessor.ReadByte(size - 1));
        }
        finally
        {
            mapped.Dispose();
        }
    }
}
