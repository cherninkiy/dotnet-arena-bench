using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArenaBench.Allocator.Core.Boundaries;

/// <summary>
/// The other direction of the boundary (ADR-002 D3): managed methods reachable
/// through the unmanaged ABI, so "native" code can call back into us without a
/// copy and without a delegate object.
/// </summary>
/// <remarks>
/// Static, taking only <see cref="IntPtr"/> and blittable values, in a
/// non-generic class - the three restrictions
/// <see cref="UnmanagedCallersOnlyAttribute"/> imposes. They are not called
/// directly: the pointers below are the only way in, which is also what the
/// tests exercise.
/// </remarks>
public static class ManagedCallbacks
{
    /// <summary>I04/I05: sum of the bytes of a buffer, reached through the unmanaged ABI.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe int Checksum(IntPtr data, int size)
    {
        byte* buffer = (byte*)data;
        int sum = 0;
        for (int index = 0; index < size; index++)
        {
            sum += buffer[index];
        }

        return sum;
    }

    /// <summary>I04/I05: index of the first zero byte of a window, or -1 - a second handler shape.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe int FindTerminator(IntPtr data, int size)
    {
        byte* buffer = (byte*)data;
        for (int index = 0; index < size; index++)
        {
            if (buffer[index] == 0)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The function pointer a native driver would receive for <see cref="Checksum"/>.</summary>
    public static unsafe delegate* unmanaged[Cdecl]<IntPtr, int, int> ChecksumPointer => &Checksum;

    /// <summary>The function pointer for <see cref="FindTerminator"/>.</summary>
    public static unsafe delegate* unmanaged[Cdecl]<IntPtr, int, int> FindTerminatorPointer => &FindTerminator;
}
