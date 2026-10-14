namespace ArenaBench.Serialization.Core.Formats;

/// <summary>
/// Order-dependent fingerprint mix shared by the T2 strategies. Benches and
/// tests only need every strategy to produce the same number for the same
/// record, so the mix is a pure function of the field sequence - and a wrong
/// offset or a missed field therefore shows up as a mismatch.
/// </summary>
public static class RecordFingerprint
{
    /// <summary>Mixes one integer field into the running fingerprint.</summary>
    public static long Mix(long hash, long value) => (hash * 31) ^ value;

    /// <summary>Mixes a 64-bit float by its exact bits, never by a rounded value.</summary>
    public static long Mix(long hash, double value) => Mix(hash, BitConverter.DoubleToInt64Bits(value));
}