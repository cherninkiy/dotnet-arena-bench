namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// Order-dependent mix shared by the T2 strategies. Benches and tests only
/// need every strategy to fold the same fields, in the same order, into the
/// same number. This is a 64-bit hash, not a checksum: collisions exist in
/// theory, so a mismatch shows that generator and reader disagree about the
/// field sequence or its offsets - it does not show which field is wrong.
/// </summary>
public static class RecordFingerprint
{
    /// <summary>Mixes one integer field into the running fingerprint.</summary>
    public static long Mix(long hash, long value) => (hash * 31) ^ value;

    /// <summary>Mixes a 64-bit float by its exact bits, never by a rounded value.</summary>
    public static long Mix(long hash, double value) => Mix(hash, BitConverter.DoubleToInt64Bits(value));
}