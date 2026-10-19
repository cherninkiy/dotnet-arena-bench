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

    private static readonly delegate* managed<IntPtr, IntPtr, nuint, void> s_memcpy;
    private static readonly delegate* managed<IntPtr, int, nuint, void> s_memset;
    private static readonly delegate* managed<IntPtr, IntPtr, nuint, int> s_memcmp;

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
    /// <exception cref="PlatformNotSupportedException">
    /// The C library or a symbol did not resolve on this platform; see
    /// <see cref="RawExportsAvailable"/>. The wrapper path above is unaffected.
    /// </exception>
    public static void RawMemcpy(IntPtr destination, IntPtr source, nuint byteCount)
    {
        ThrowIfRawUnavailable();
        s_rawMemcpy(destination, source, byteCount);
    }

    /// <summary>Raw export of memset (ADR-002 D2, measured by I09).</summary>
    /// <inheritdoc cref="RawMemcpy"/>
    public static void RawMemset(IntPtr destination, int value, nuint byteCount)
    {
        ThrowIfRawUnavailable();
        s_rawMemset(destination, value, byteCount);
    }

    /// <summary>Raw export of memcmp (ADR-002 D2, measured by I09).</summary>
    /// <inheritdoc cref="RawMemcpy"/>
    public static int RawMemcmp(IntPtr lhs, IntPtr rhs, nuint byteCount)
    {
        ThrowIfRawUnavailable();
        return s_rawMemcmp(lhs, rhs, byteCount);
    }

    /// <summary>
    /// Whether the raw exports resolved here. The managed wrapper path never
    /// depends on them: the raw path is best-effort by design, so a platform where
    /// the library or one of the symbols is missing loses the I09 baseline instead
    /// of the whole boundary series.
    /// </summary>
    public static bool RawExportsAvailable => s_rawAvailable;

    /// <summary>
    /// Candidates for the raw path, tried in order.
    /// <see cref="NativeLibrary.Load(string)"/> takes its argument as a file name:
    /// the platform suffix probing that <see cref="LibraryImportAttribute"/> does
    /// (lib/dl/dylib, version suffixes) is **not** applied, so the bare names that
    /// work for the declarations above do not resolve here - macOS needs the
    /// canonical <c>/usr/lib/libSystem.B.dylib</c>, Linux the soname, Windows the
    /// <c>.dll</c>.
    /// </summary>
    private static readonly string[] s_rawCandidates = RawCandidates();

    private static readonly IntPtr s_library;
    private static readonly bool s_rawAvailable;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, void> s_rawMemcpy;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, int, nuint, void> s_rawMemset;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, int> s_rawMemcmp;

    /// <summary>
    /// Resolves everything in one explicit order: field initializers would depend
    /// on their declaration order, and a reordered field would turn a refactoring
    /// slip into a <see cref="TypeInitializationException"/> on the first call
    /// instead of a visible dependency here. The raw part is best-effort - a
    /// missing library or symbol leaves <see cref="RawExportsAvailable"/> false
    /// and does not stop the type from initializing.
    /// </summary>
    static NativeMethods()
    {
        s_memcpy = SelectMemcpy();
        s_memset = SelectMemset();
        s_memcmp = SelectMemcmp();

        s_library = LoadFirst(s_rawCandidates);
        IntPtr rawMemcpy = TryResolveExport("memcpy");
        IntPtr rawMemset = TryResolveExport("memset");
        IntPtr rawMemcmp = TryResolveExport("memcmp");

        s_rawAvailable = rawMemcpy != IntPtr.Zero && rawMemset != IntPtr.Zero && rawMemcmp != IntPtr.Zero;
        if (s_rawAvailable)
        {
            s_rawMemcpy = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, void>)rawMemcpy;
            s_rawMemset = (delegate* unmanaged[Cdecl]<IntPtr, int, nuint, void>)rawMemset;
            s_rawMemcmp = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, int>)rawMemcmp;
        }
    }

    private static string[] RawCandidates()
        => LibcResolver.IsWindows ? ["ucrtbase.dll", "ucrtbase", "msvcrt.dll"]
         : LibcResolver.IsMacOS ? ["/usr/lib/libSystem.B.dylib", "libSystem.dylib", "libSystem"]
         : ["libc.so.6", "libc", "libc.so"];

    private static IntPtr LoadFirst(string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr TryResolveExport(string symbol)
    {
        if (s_library == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (NativeLibrary.TryGetExport(s_library, symbol, out IntPtr address))
        {
            return address;
        }

        // The Mach-O symbol table carries C symbols with a leading underscore.
        return NativeLibrary.TryGetExport(s_library, "_" + symbol, out address) ? address : IntPtr.Zero;
    }

    private static void ThrowIfRawUnavailable()
    {
        if (s_rawAvailable)
        {
            return;
        }

        throw new PlatformNotSupportedException(
            $"The raw libc exports did not resolve on this platform (tried {string.Join(", ", s_rawCandidates)}); "
            + "the LibraryImport wrapper path is unaffected, see NativeMethods.RawExportsAvailable.");
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
