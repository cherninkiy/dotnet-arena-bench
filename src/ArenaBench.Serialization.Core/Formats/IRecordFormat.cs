namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// A T2 record type described as a trait: wire size, deterministic generator,
/// encoder and the two field-by-field readers. The members are static
/// abstract (C# 11+), so the strategy matrix can call them from a generic
/// context without delegates - the measured loop stays free of indirection.
/// </summary>
/// <typeparam name="TRecord">Unmanaged record type from <c>Structures/</c>.</typeparam>
public interface IRecordFormat<TRecord>
    where TRecord : unmanaged
{
    /// <summary>Wire size of one record: 16, 32, 64, 128 or 256 bytes.</summary>
    static abstract int SizeInBytes { get; }

    /// <summary>
    /// Deterministic record for the given index; the fixed seed semantics of
    /// concept.md 7.4, shared by benches and tests.
    /// </summary>
    static abstract TRecord Create(int recordIndex);

    /// <summary>Encodes one record, including its bulk region, in the requested byte order.</summary>
    static abstract void WriteRecord(in TRecord record, Span<byte> destination, ByteOrder order);

    /// <summary>
    /// Reads one record field by field with <see cref="BinaryReader"/> (S01-S04)
    /// and drains the bulk region that no primitive read consumes.
    /// <paramref name="scratch"/> must hold at least <see cref="SizeInBytes"/>
    /// bytes; it is owned by the caller so that the baseline does not allocate
    /// per record.
    /// </summary>
    static abstract long ReadBinaryReader(BinaryReader reader, Span<byte> scratch);

    /// <summary>Reads one record field by field with <c>BinaryPrimitives</c> (S05-S06).</summary>
    static abstract long ReadPrimitives(ReadOnlySpan<byte> source, ByteOrder order);

    /// <summary>
    /// Order-independent fingerprint of a record. Strategies sum it per record,
    /// which makes their results comparable and keeps the JIT from eliminating
    /// the reads as dead code.
    /// </summary>
    static abstract long Checksum(in TRecord record);
}