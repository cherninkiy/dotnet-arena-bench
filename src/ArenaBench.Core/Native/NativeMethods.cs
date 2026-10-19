using System.Runtime.InteropServices;

namespace ArenaBench.Core.Native;

/// <summary>
/// libc P/Invoke entry points for boundary scenarios (ADR-002 D2):
/// memcpy — native code reads our memory; memset — native code writes into
/// our memory; memcmp — verification. Library names resolve per OS through
/// <see cref="LibcResolver"/> (ucrtbase / libSystem / libc). The per-OS choice
/// is made once at type initialization into a function pointer, so the
/// boundary hot path carries no per-call OS dispatch.
/// </summary>
internal static unsafe partial class NativeMethods
{
    // One declaration per OS: LibraryImport needs compile-time constant names,
    // so the platform cannot be folded into a single attribute.

    [LibraryImport(LibcResolver.LinuxLibc, EntryPoint = "memcpy", SetLastError = false)]
    private static partial void MemcpyLinux(IntPtr destination, IntPtr source, nuint byteCount);

    [LibraryImport(LibcResolver.MacLibSystem, EntryPoint = "memcpy", SetLastError = false)]
    private static partial void MemcpyMac(IntPtr destination, IntPtr source, nuint byteCount);

    [LibraryImport(LibcResolver.WindowsCrt, EntryPoint = "memcpy", SetLastError = false)]
    private static partial void MemcpyWin(IntPtr destination, IntPtr source, nuint byteCount);

    [LibraryImport(LibcResolver.LinuxLibc, EntryPoint = "memset", SetLastError = false)]
    private static partial void MemsetLinux(IntPtr destination, int value, nuint byteCount);

    [LibraryImport(LibcResolver.MacLibSystem, EntryPoint = "memset", SetLastError = false)]
    private static partial void MemsetMac(IntPtr destination, int value, nuint byteCount);

    [LibraryImport(LibcResolver.WindowsCrt, EntryPoint = "memset", SetLastError = false)]
    private static partial void MemsetWin(IntPtr destination, int value, nuint byteCount);

    [LibraryImport(LibcResolver.LinuxLibc, EntryPoint = "memcmp", SetLastError = false)]
    private static partial int MemcmpLinux(IntPtr lhs, IntPtr rhs, nuint byteCount);

    [LibraryImport(LibcResolver.MacLibSystem, EntryPoint = "memcmp", SetLastError = false)]
    private static partial int MemcmpMac(IntPtr lhs, IntPtr rhs, nuint byteCount);

    [LibraryImport(LibcResolver.WindowsCrt, EntryPoint = "memcmp", SetLastError = false)]
    private static partial int MemcmpWin(IntPtr lhs, IntPtr rhs, nuint byteCount);

    private static readonly delegate* managed<IntPtr, IntPtr, nuint, void> s_memcpy = SelectMemcpy();
    private static readonly delegate* managed<IntPtr, int, nuint, void> s_memset = SelectMemset();
    private static readonly delegate* managed<IntPtr, IntPtr, nuint, int> s_memcmp = SelectMemcmp();

    /// <summary>Native reads from our memory: memcpy (ADR-002 D2).</summary>
    public static void Memcpy(IntPtr destination, IntPtr source, nuint byteCount)
        => s_memcpy(destination, source, byteCount);

    /// <summary>Native writes into our memory: memset (ADR-002 D2).</summary>
    public static void Memset(IntPtr destination, int value, nuint byteCount)
        => s_memset(destination, value, byteCount);

    /// <summary>Buffer comparison for verification: memcmp (ADR-002 D2).</summary>
    public static int Memcmp(IntPtr lhs, IntPtr rhs, nuint byteCount)
        => s_memcmp(lhs, rhs, byteCount);

    /// <summary>
    /// Raw export of memcpy: the C entry point itself, without the managed
    /// wrapper the JIT builds around a <see cref="LibraryImportAttribute"/>
    /// method. ADR-002 D2 left the question of what that wrapper costs to PoC-2,
    /// and I09 measures it (the returned pointer of C memcpy is ignored on
    /// purpose: it is ABI-compatible with a void return).
    /// </summary>
    public static void RawMemcpy(IntPtr destination, IntPtr source, nuint byteCount)
        => s_rawMemcpy(destination, source, byteCount);

    /// <summary>Raw export of memset (ADR-002 D2, measured by I09).</summary>
    public static void RawMemset(IntPtr destination, int value, nuint byteCount)
        => s_rawMemset(destination, value, byteCount);

    /// <summary>Raw export of memcmp (ADR-002 D2, measured by I09).</summary>
    public static int RawMemcmp(IntPtr lhs, IntPtr rhs, nuint byteCount)
        => s_rawMemcmp(lhs, rhs, byteCount);

    // Loaded once and kept for the process lifetime: the raw path exists to be
    // measured, not to manage handles. Declaration order matters - the pointer
    // initializers below call ResolveExport, which needs the handle.
    private static readonly IntPtr s_library = NativeLibrary.Load(LibraryName());

    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, void> s_rawMemcpy =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, void>)ResolveExport("memcpy");

    private static readonly delegate* unmanaged[Cdecl]<IntPtr, int, nuint, void> s_rawMemset =
        (delegate* unmanaged[Cdecl]<IntPtr, int, nuint, void>)ResolveExport("memset");

    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, int> s_rawMemcmp =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, int>)ResolveExport("memcmp");

    private static string LibraryName()
        => LibcResolver.IsWindows ? LibcResolver.WindowsCrt
         : LibcResolver.IsMacOS ? LibcResolver.MacLibSystem
         : LibcResolver.LinuxLibc;

    private static IntPtr ResolveExport(string symbol)
    {
        if (NativeLibrary.TryGetExport(s_library, symbol, out IntPtr address))
        {
            return address;
        }

        // The Mach-O symbol table carries C symbols with a leading underscore.
        if (NativeLibrary.TryGetExport(s_library, "_" + symbol, out address))
        {
            return address;
        }

        throw new EntryPointNotFoundException(
            $"Export {symbol} was not found in {LibraryName()} (ADR-002 D2).");
    }

    private static delegate* managed<IntPtr, IntPtr, nuint, void> SelectMemcpy()
        => LibcResolver.IsWindows ? &MemcpyWin
         : LibcResolver.IsMacOS ? &MemcpyMac
         : &MemcpyLinux;

    private static delegate* managed<IntPtr, int, nuint, void> SelectMemset()
        => LibcResolver.IsWindows ? &MemsetWin
         : LibcResolver.IsMacOS ? &MemsetMac
         : &MemsetLinux;

    private static delegate* managed<IntPtr, IntPtr, nuint, int> SelectMemcmp()
        => LibcResolver.IsWindows ? &MemcmpWin
         : LibcResolver.IsMacOS ? &MemcmpMac
         : &MemcmpLinux;
}
