using System.Runtime.InteropServices;

namespace ArenaBench.Core.Native;

/// <summary>
/// Resolves the C runtime library names per OS (ADR-002 D2):
/// Windows — ucrtbase, macOS — libSystem, Linux — libc. Bare "libc" is not
/// reliable on modern macOS (dotnet/sdk #6642), hence the explicit libSystem.
/// <see cref="LibraryImportAttribute"/> needs compile-time constant names, so
/// the choice between the three declarations lives in
/// <see cref="NativeMethods"/>, where it happens once at type initialization.
/// </summary>
internal static class LibcResolver
{
    internal const string LinuxLibc = "libc";
    internal const string MacLibSystem = "libSystem";
    internal const string WindowsCrt = "ucrtbase";

    internal static readonly bool IsWindows =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    internal static readonly bool IsMacOS =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}
