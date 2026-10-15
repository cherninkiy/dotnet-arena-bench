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
}
