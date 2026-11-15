using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry
{
    /// <summary>One outline repair attempt, for the grid, the log and the Excel report.</summary>
    /// <param name="Method">Method number: 1 boundary location, 2 clean, 3 merge, 4 snap, 5 bridge, 6 overlaps, 7 arcs, 8 3D shape, 9 skip hole, 10 approximate.</param>
    /// <param name="Name">Short method name.</param>
    /// <param name="Passed">True when the outline was closed after this attempt.</param>
    /// <param name="Detail">What was found or changed.</param>
    /// <param name="At">Where, when the attempt has a position.</param>
    public sealed record RepairStep(int Method, string Name, bool Passed, string Detail, Pt? At);

    /// <summary>Result of repairing one loop.</summary>
    /// <param name="Points">Closed outline, counter-clockwise, first point not repeated; empty when not <paramref name="Ok"/>.</param>
    /// <param name="Ok">True when the outline is closed and does not cross itself.</param>
    /// <param name="Steps">Every repair attempted, in order; empty when none was needed.</param>
    /// <param name="Approximate">True when the outline is a hull, not the real boundary.</param>
    public sealed record LoopResult(IReadOnlyList<Pt> Points, bool Ok, IReadOnlyList<RepairStep> Steps, bool Approximate);

    /// <summary>The outline a roof is made from, with how it was obtained.</summary>
    /// <param name="Outer">Outer outline, counter-clockwise; empty when the room failed.</param>
    /// <param name="Inner">Holes (columns, shafts) that could be closed.</param>
    /// <param name="AreaM2">Outer area minus the holes, m².</param>
    /// <param name="RoomAreaM2">The room's own area, m².</param>
    /// <param name="AreaDiffPct">(outline − room) / room, percent; 0 when the room area is unknown.</param>
    /// <param name="Status">Ready, Repaired, AreaWarning, Approximate, NotEnclosed or Failed.</param>
    /// <param name="Steps">Every repair attempted on any loop of the room.</param>
    public sealed record RoomOutline(
        IReadOnlyList<Pt> Outer,
        IReadOnlyList<IReadOnlyList<Pt>> Inner,
        double AreaM2,
        double RoomAreaM2,
        double AreaDiffPct,
        RoomStatus Status,
        IReadOnlyList<RepairStep> Steps);
}
