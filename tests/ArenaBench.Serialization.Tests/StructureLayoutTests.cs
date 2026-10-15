using System.Runtime.CompilerServices;

using ArenaBench.Serialization.Core.Formats;
using ArenaBench.Serialization.Core.Structures;

namespace ArenaBench.Serialization.Tests;

/// <summary>
/// The size ladder of PoC-1 (concept.md 5.1). The wire image of a record has
/// no padding by construction, so these assertions are what keeps it that way
/// when a field is added, widened or reordered.
/// </summary>
public sealed class StructureLayoutTests
{
    [Fact]
    public void RecordSizes_AreTheDocumentedLadder()
    {
        Assert.Equal(16, Unsafe.SizeOf<ServiceRecord16>());
        Assert.Equal(32, Unsafe.SizeOf<Commodity32>());
        Assert.Equal(64, Unsafe.SizeOf<VideoRecord64>());
        Assert.Equal(128, Unsafe.SizeOf<ExtendedRecord128>());
        Assert.Equal(256, Unsafe.SizeOf<MultiFieldRecord256>());
    }

    [Fact]
    public void Formats_AgreeWithTheRecordSizes()
    {
        Assert.Equal(ServiceRecord16Format.Size, ServiceRecord16Format.SizeInBytes);
        Assert.Equal(Commodity32Format.Size, Commodity32Format.SizeInBytes);
        Assert.Equal(VideoRecord64Format.Size, VideoRecord64Format.SizeInBytes);
        Assert.Equal(ExtendedRecord128Format.Size, ExtendedRecord128Format.SizeInBytes);
        Assert.Equal(MultiFieldRecord256Format.Size, MultiFieldRecord256Format.SizeInBytes);
    }

    [Fact]
    public void BulkRegions_FillExactlyTheRestOfTheirRecord()
    {
        Assert.Equal(VideoRecord64Format.Size, VideoRecord64Format.PayloadOffset + VideoRecord64Format.PayloadSize);
        Assert.Equal(ExtendedRecord128Format.Size, ExtendedRecord128Format.PayloadOffset + ExtendedRecord128Format.PayloadSize);
        Assert.Equal(MultiFieldRecord256Format.Size, MultiFieldRecord256Format.PayloadOffset + MultiFieldRecord256Format.PayloadSize);
    }
}