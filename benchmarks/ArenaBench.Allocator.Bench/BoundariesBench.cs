using ArenaBench.Allocator.Core.Boundaries;
using ArenaBench.Core;

using BenchmarkDotNet.Attributes;

namespace ArenaBench.Allocator.Bench;

/// <summary>
/// I01-I09: the system boundary of concept.md 5.2 (ADR-002). One axis only -
/// where our buffer lives. An arena block needs no pinning, a managed array has
/// to be pinned on every call (<c>fixed</c>), and that pinning is the cost the
/// arena is supposed to remove. Both sides call the same libc function through
/// the same scenario code.
/// </summary>
/// <remarks>
/// The sink and the egress buffer are arena blocks as well: a native peer reads
/// and writes raw pointers, so pinning them would measure the harness instead of
/// the case. I09 repeats I01/I02 through a raw export, which is the question
/// ADR-002 D2 left to PoC-2: what the managed LibraryImport wrapper costs. I08
/// adds the file mapping of ADR-002 D4, where the arena runs over memory it does
/// not own.
/// </remarks>
public class BoundariesBench
{
    /// <summary>I05: callback invocations per operation.</summary>
    private const int CallbackCalls = 64;

    private Arena _arena = null!;
    private IntPtr _ours;
    private IntPtr _other;
    private IntPtr _egress;
    private byte[] _managed = null!;

    private MappedMemory _mapped = null!;
    private Arena _mappedArena = null!;
    private Arena _nativeArena = null!;
    private byte[] _staging = null!;

    /// <summary>Buffer size in bytes (concept.md 5.2): 32 B, 1 KiB, 64 KiB.</summary>
    [Params(32, 1024, 65536)]
    public int SizeBytes { get; set; }

    [GlobalSetup]
    public unsafe void Setup()
    {
        // Our buffer, the sink and the connection buffer share one block: the case
        // to measure is the transit, not the arena's own allocation.
        _arena = new Arena((nuint)(SizeBytes * 3) + 512);
        _ours = _arena.AllocPtr<byte>(SizeBytes);
        _other = _arena.AllocPtr<byte>(SizeBytes);
        _egress = _arena.AllocPtr<byte>(SizeBytes);

        // Deterministic contents: arena memory is not zeroed (ADR-001 D5).
        new Span<byte>((void*)_ours, SizeBytes).Fill(0x11);
        new Span<byte>((void*)_other, SizeBytes).Clear();
        new Span<byte>((void*)_egress, SizeBytes).Clear();

        _managed = new byte[SizeBytes];
        _managed.AsSpan().Fill(0x11);

        _mapped = new MappedMemory(SizeBytes);
        _mappedArena = _mapped.CreateArena();
        _nativeArena = new Arena((nuint)SizeBytes + 64);
        _staging = new byte[SizeBytes];
        _staging.AsSpan().Fill(0x22);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nativeArena.Dispose();
        _mappedArena.Dispose(); // does not own the mapping: the arena only borrows it
        _mapped.Dispose();
        _arena.Dispose();
    }

    /// <summary>I01 baseline: the arena block goes to native code without any pinning.</summary>
    [Benchmark(Baseline = true)]
    public long I01_Arena_NativeReadsOurs() =>
        PInvokeScenarios.NativeReadsOurs(_ours, _other, SizeBytes);

    /// <summary>I01: the same call over a managed array, pinned for the call.</summary>
    [Benchmark]
    public unsafe long I01_PinnedArray_NativeReadsOurs()
    {
        fixed (byte* pinned = _managed)
        {
            return PInvokeScenarios.NativeReadsOurs((IntPtr)pinned, _other, SizeBytes);
        }
    }

    /// <summary>I02: native writes the arena block (memset) and we read it back.</summary>
    [Benchmark]
    public unsafe long I02_Arena_NativeWritesOurs() => PInvokeScenarios.NativeWritesOurs(_ours, SizeBytes);

    /// <summary>I02: the same memset over a managed array.</summary>
    [Benchmark]
    public unsafe long I02_PinnedArray_NativeWritesOurs()
    {
        fixed (byte* pinned = _managed)
        {
            return PInvokeScenarios.NativeWritesOurs((IntPtr)pinned, SizeBytes);
        }
    }

    /// <summary>I03: inout - native copies our buffer into our second block and verifies it.</summary>
    [Benchmark]
    public int I03_Arena_InOut() => PInvokeScenarios.InOut(_ours, _other, SizeBytes);

    /// <summary>I03: the same round trip over a pinned managed array.</summary>
    [Benchmark]
    public unsafe int I03_PinnedArray_InOut()
    {
        fixed (byte* pinned = _managed)
        {
            return PInvokeScenarios.InOut((IntPtr)pinned, _other, SizeBytes);
        }
    }

    /// <summary>I09: the same memcpy through the raw libc export - no managed wrapper.</summary>
    [Benchmark]
    public long I09_Arena_RawExportMemcpy() =>
        PInvokeScenarios.NativeReadsOursRaw(_ours, _other, SizeBytes);

    /// <summary>I09: the same memset through the raw libc export.</summary>
    [Benchmark]
    public unsafe long I09_Arena_RawExportMemset() =>
        PInvokeScenarios.NativeWritesOursRaw(_ours, SizeBytes);

    /// <summary>I04: a managed handler is called once through the unmanaged ABI, with no copy.</summary>
    [Benchmark]
    public unsafe long I04_Arena_CallbackOnce() => NativeCallbackScenarios.InvokeOnce(_ours, SizeBytes);

    /// <summary>I04: the same callback over a pinned managed array.</summary>
    [Benchmark]
    public unsafe long I04_PinnedArray_CallbackOnce()
    {
        fixed (byte* pinned = _managed)
        {
            return NativeCallbackScenarios.InvokeOnce((IntPtr)pinned, SizeBytes);
        }
    }

    /// <summary>I05: 64 invocations per operation over equal windows of the arena block.</summary>
    [Benchmark]
    public unsafe long I05_Arena_CallbackRepeated() =>
        NativeCallbackScenarios.InvokeRepeatedly(_ours, SizeBytes, CallbackCalls);

    /// <summary>I05: the same repeated calls over a pinned managed array.</summary>
    [Benchmark]
    public unsafe long I05_PinnedArray_CallbackRepeated()
    {
        fixed (byte* pinned = _managed)
        {
            return NativeCallbackScenarios.InvokeRepeatedly((IntPtr)pinned, SizeBytes, CallbackCalls);
        }
    }

    /// <summary>I06: egress - native code copies the arena payload into the connection buffer.</summary>
    [Benchmark]
    public long I06_Arena_Egress() => PInvokeScenarios.Egress(_ours, _egress, SizeBytes);

    /// <summary>I06: the same egress out of a pinned managed array.</summary>
    [Benchmark]
    public unsafe long I06_PinnedArray_Egress()
    {
        fixed (byte* pinned = _managed)
        {
            return PInvokeScenarios.Egress((IntPtr)pinned, _egress, SizeBytes);
        }
    }

    /// <summary>I07: egress plus native verification of what arrived.</summary>
    [Benchmark]
    public long I07_Arena_EgressVerified()
    {
        _ = PInvokeScenarios.Egress(_ours, _egress, SizeBytes);
        return PInvokeScenarios.VerifyEgress(_ours, _egress, SizeBytes);
    }

    /// <summary>I07: the same egress and verification out of a pinned managed array.</summary>
    [Benchmark]
    public unsafe long I07_PinnedArray_EgressVerified()
    {
        fixed (byte* pinned = _managed)
        {
            _ = PInvokeScenarios.Egress((IntPtr)pinned, _egress, SizeBytes);
            return PInvokeScenarios.VerifyEgress((IntPtr)pinned, _egress, SizeBytes);
        }
    }

    /// <summary>I08 baseline: the BCL accessor writes the staging array into the mapped view.</summary>
    [Benchmark]
    public long I08_MmfAccessor_WriteArray()
    {
        _mapped.Accessor.WriteArray(0, _staging, 0, SizeBytes);
        return _mapped.Accessor.ReadByte(SizeBytes - 1);
    }

    /// <summary>I08: the same write through an arena over the file mapping (ADR-002 D4).</summary>
    [Benchmark]
    public long I08_ArenaOverMmap_Bump()
    {
        _mappedArena.Reset();
        Span<byte> block = _mappedArena.AllocSpan<byte>(SizeBytes);
        _staging.CopyTo(block);
        return block[SizeBytes - 1];
    }

    /// <summary>I08: the identical bump over native memory - isolates what the mapping costs.</summary>
    [Benchmark]
    public long I08_ArenaNative_Bump()
    {
        _nativeArena.Reset();
        Span<byte> block = _nativeArena.AllocSpan<byte>(SizeBytes);
        _staging.CopyTo(block);
        return block[SizeBytes - 1];
    }
}
