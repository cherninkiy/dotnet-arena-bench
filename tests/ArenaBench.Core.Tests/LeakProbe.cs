namespace ArenaBench.Core.Tests;

/// <summary>
/// Shared probe for the leak assertions: <c>SafeArenaHandle.LiveCount</c> is
/// process-global, so every test that compares it has to settle the finalizers
/// first and read it the same way.
/// </summary>
internal static class LeakProbe
{
    /// <summary>Arena (and <c>SafeArenaHandle</c>) count once the finalizers have run.</summary>
    public static int LiveCount()
    {
        CollectAndWaitForFinalizers();
        return SafeArenaHandle.LiveCount;
    }

    /// <summary>Repeats the collection: one pass can leave the next generation of finalizable objects behind.</summary>
    public static void CollectAndWaitForFinalizers(int attempts = 20)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
