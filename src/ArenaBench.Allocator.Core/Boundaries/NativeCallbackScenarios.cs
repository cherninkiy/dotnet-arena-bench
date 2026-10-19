using System.Runtime.CompilerServices;

namespace ArenaBench.Allocator.Core.Boundaries;

/// <summary>
/// I04/I05: the callback boundary - "native" code drives a managed handler
/// through the unmanaged ABI. ADR-002 D3 fixes the shape: the loop around the
/// pointer stays managed (a native driver library is out of scope), so the case
/// prices the transit per call, and both memory variants cross it identically.
/// </summary>
public static class NativeCallbackScenarios
{
    /// <summary>I04: one invocation over the whole buffer.</summary>
    public static unsafe int InvokeOnce(IntPtr data, int size)
    {
        delegate* unmanaged[Cdecl]<IntPtr, int, int> handle = ManagedCallbacks.ChecksumPointer;
        return handle(data, size);
    }

    /// <summary>I05: <paramref name="calls"/> invocations over equal windows of the buffer.</summary>
    public static unsafe long InvokeRepeatedly(IntPtr data, int size, int calls)
    {
        delegate* unmanaged[Cdecl]<IntPtr, int, int> handle = ManagedCallbacks.ChecksumPointer;
        int window = size / calls;
        long sum = 0;
        for (int call = 0; call < calls; call++)
        {
            sum += handle(IntPtr.Add(data, call * window), window);
        }

        return sum;
    }

    /// <summary>I04: the second handler shape, exercised by the verification tests.</summary>
    public static unsafe int FindTerminator(IntPtr data, int size)
    {
        delegate* unmanaged[Cdecl]<IntPtr, int, int> handle = ManagedCallbacks.FindTerminatorPointer;
        return handle(data, size);
    }
}
