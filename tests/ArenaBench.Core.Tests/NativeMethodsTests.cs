using System.Runtime.InteropServices;
using ArenaBench.Core;
using ArenaBench.Core.Native;
using Xunit;

namespace ArenaBench.Core.Tests;

// Locks the P/Invoke boundary path (ADR-002 D2): the per-OS library names
// (ucrtbase / libSystem / libc) and the one-time function-pointer selection
// must actually resolve and round-trip on every OS in the CI matrix.
[Collection("Arena")]
public class NativeMethodsTests
{
    [Fact]
    public void Memcpy_Memset_Memcmp_RoundTrip()
    {
        using var arena = new Arena(256);
        IntPtr source = arena.AllocPtr<byte>(64);
        IntPtr destination = arena.AllocPtr<byte>(64);

        NativeMethods.Memset(source, 0xAB, 64);
        Assert.Equal(0xAB, Marshal.ReadByte(source, 0));
        Assert.Equal(0xAB, Marshal.ReadByte(source, 63));

        NativeMethods.Memcpy(destination, source, 64);
        Assert.Equal(0, NativeMethods.Memcmp(destination, source, 64));
        Assert.Equal(0xAB, Marshal.ReadByte(destination, 63));

        NativeMethods.Memset(destination, 0x00, 64);
        Assert.NotEqual(0, NativeMethods.Memcmp(destination, source, 64));
        Assert.Equal(0xAB, Marshal.ReadByte(source, 63)); // source stays intact
    }

    /// <summary>
    /// The same round trip for tiny buffers: memcpy/memset/memcmp have to work for
    /// sizes below a machine word too, and a platform where the export name is wrong
    /// fails here rather than in the middle of a benchmark.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(64)]
    public void Memcpy_Memcmp_RoundTrip_ForSmallSizes(int size)
    {
        using var arena = new Arena(256);
        IntPtr source = arena.AllocPtr<byte>(size);
        IntPtr destination = arena.AllocPtr<byte>(size);

        NativeMethods.Memset(source, 0x5A, (nuint)size);
        NativeMethods.Memcpy(destination, source, (nuint)size);

        Assert.Equal(0, NativeMethods.Memcmp(destination, source, (nuint)size));
        Assert.Equal(0x5A, Marshal.ReadByte(destination, size - 1));

        NativeMethods.Memset(destination, 0x00, (nuint)size);
        Assert.NotEqual(0, NativeMethods.Memcmp(destination, source, (nuint)size));
    }

    /// <summary>
    /// The raw path bypasses the runtime's library probing (<c>NativeLibrary.Load</c>
    /// takes a file name), so it resolves through an explicit candidate list. On
    /// Windows and Linux it must resolve - otherwise I09 silently loses its
    /// baseline. On macOS the canonical <c>/usr/lib/libSystem.B.dylib</c> is not
    /// guaranteed to be loadable from every runtime configuration, so the state is
    /// left to <see cref="NativeMethods.RawExportsAvailable"/>: the wrapper path
    /// above is what the boundary depends on.
    /// </summary>
    [Fact]
    public void RawExports_ResolveWhereTheLibraryNameIsKnown()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.True(
            NativeMethods.RawExportsAvailable,
            "the raw libc exports must resolve on Windows and Linux (ADR-002 D2, case I09)");
    }

    /// <summary>Same round trip as above, through the raw exports (I09's path).</summary>
    [Fact]
    public void RawExports_RoundTripLikeTheWrapper()
    {
        if (!NativeMethods.RawExportsAvailable)
        {
            return;
        }

        using var arena = new Arena(256);
        IntPtr buffer = arena.AllocPtr<byte>(32);
        IntPtr copy = arena.AllocPtr<byte>(32);

        NativeMethods.RawMemset(buffer, 0x7E, 32);
        NativeMethods.RawMemcpy(copy, buffer, 32);

        Assert.Equal(0, NativeMethods.RawMemcmp(copy, buffer, 32));
        Assert.Equal(0x7E, Marshal.ReadByte(copy, 31));
    }
}
