using ArenaBench.Core.Native;

namespace ArenaBench.Allocator.Core.Boundaries;

/// <summary>
/// The libc scenarios of concept.md 5.2 (ADR-002 D2): memcpy reads our memory,
/// memset writes it, memcmp verifies it. Every method takes
/// <see cref="IntPtr"/> and nothing else, so an arena block and a pinned managed
/// array run through exactly the same code - the only variable left is where the
/// memory lives and what pinning it costs.
/// </summary>
/// <remarks>
/// The Raw variants run the same two calls through the raw exports. That is the
/// question ADR-002 D2 left open: how much of the measured cost is the managed
/// wrapper the JIT builds around a <see cref="System.Runtime.InteropServices.LibraryImportAttribute"/>
/// method.
/// </remarks>
public static class PInvokeScenarios
{
    /// <summary>I01: native reads our buffer - memcpy from our memory into a sink.</summary>
    public static long NativeReadsOurs(IntPtr ours, IntPtr sink, int size)
    {
        NativeMethods.Memcpy(sink, ours, (nuint)size);
        return size;
    }

    /// <summary>I01 (raw export): the same call with no managed wrapper in the path.</summary>
    public static long NativeReadsOursRaw(IntPtr ours, IntPtr sink, int size)
    {
        NativeMethods.RawMemcpy(sink, ours, (nuint)size);
        return size;
    }

    /// <summary>I02: native writes our buffer - memset, then a read-back so the fill cannot be dropped.</summary>
    public static unsafe long NativeWritesOurs(IntPtr ours, int size)
    {
        NativeMethods.Memset(ours, 0x5A, (nuint)size);
        byte* buffer = (byte*)ours;
        return buffer[0] + buffer[size - 1];
    }

    /// <summary>I02 (raw export): the same memset with no managed wrapper in the path.</summary>
    public static unsafe long NativeWritesOursRaw(IntPtr ours, int size)
    {
        NativeMethods.RawMemset(ours, 0x5A, (nuint)size);
        byte* buffer = (byte*)ours;
        return buffer[0] + buffer[size - 1];
    }

    /// <summary>I03: inout - native copies our buffer into a second block of ours and verifies it.</summary>
    public static int InOut(IntPtr ours, IntPtr other, int size)
    {
        NativeMethods.Memcpy(other, ours, (nuint)size);
        return NativeMethods.Memcmp(other, ours, (nuint)size);
    }

    /// <summary>I06: egress - native code copies our payload into the connection buffer.</summary>
    public static long Egress(IntPtr payload, IntPtr connectionBuffer, int size)
    {
        NativeMethods.Memcpy(connectionBuffer, payload, (nuint)size);
        return size;
    }

    /// <summary>I07: egress verification - native compares what left with what is still ours.</summary>
    public static int VerifyEgress(IntPtr payload, IntPtr connectionBuffer, int size)
        => NativeMethods.Memcmp(connectionBuffer, payload, (nuint)size);
}
