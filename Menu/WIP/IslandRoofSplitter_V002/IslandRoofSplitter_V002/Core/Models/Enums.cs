// IslandRoofSplitter V002 — option enums. Member names are matched by EnumToBoolConverter (ToString), keep them stable.
#nullable enable
namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Models
{
    public enum SplitMode { Flat, Sloped }

    /// <summary>Per Anas's decision (2026-10-14): both offered in the UI.</summary>
    public enum NestingMode
    {
        /// <summary>Outer region gets an opening where an inner island sits (no overlap).</summary>
        Ring,
        /// <summary>Outer region is the full loop; inner island overlaps it.</summary>
        Full
    }

    /// <summary>Per Anas's decision (2026-10-14): both offered in the UI.</summary>
    public enum CrossingMode
    {
        /// <summary>Lines crossing mid-span are split at the crossing (T-junctions too).</summary>
        Split,
        /// <summary>Only lines joined end-to-end form loops; crossings make a loop self-intersecting.</summary>
        ChainsOnly
    }

    /// <summary>Flat mode only. Sloped mode always tessellates (every vertex needs a Z).</summary>
    public enum ArcOutput { TrueArcs, Tessellate }

    /// <summary>Sloped mode only.</summary>
    public enum InteriorPoints
    {
        BoundaryOnly,
        /// <summary>Also add shape points where the parent's ridges/valleys cross or sit inside the island.</summary>
        RidgeValleyPoints
    }

    /// <summary>
    /// What V002 creates per island. Per Anas (2026-10-15): a roof is the point of the tool, but a footprint
    /// Revit refuses as a roof is usually still valid as a floor slab — so the fallback is offered in the UI.
    /// </summary>
    public enum OutputElement
    {
        /// <summary>Roof only. An island Revit refuses is reported as Failed (V001 behaviour).</summary>
        Roof,
        /// <summary>Try the roof; if every roof attempt fails, create a floor with the same footprint.</summary>
        RoofThenFloor,
        /// <summary>Floor only — never attempt a roof.</summary>
        Floor
    }

    /// <summary>What actually got created for an island.</summary>
    public enum ElementKind { None, Roof, Floor }

    public enum IslandStatus { Ready, Skipped, Created, Failed }

    /// <summary>Quick-filter chips over the results grid.</summary>
    public enum IslandFilter { All, Ready, Skipped, Created, Failed }

    public enum MessageSeverity { Debug, Info, Warning, Error, Success }
}
