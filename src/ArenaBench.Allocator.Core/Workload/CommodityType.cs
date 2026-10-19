namespace ArenaBench.Allocator.Core.Workload;

/// <summary>
/// The three commodity types of the mixed workload (concept.md 2, 5.2): voice
/// (T1-like frames), service records (T2-like fixed layout) and text (T3-like).
/// Each type brings its own payload shape and its own share of the work, which
/// is exactly what makes per-type arenas worth testing against one shared arena.
/// </summary>
public enum CommodityType
{
    /// <summary>T1-like voice frames: a short header plus a payload.</summary>
    Voice = 0,

    /// <summary>T2-like service records: a fixed 32 B layout.</summary>
    Service = 1,

    /// <summary>T3-like text: a UTF-8 message.</summary>
    Text = 2,
}
