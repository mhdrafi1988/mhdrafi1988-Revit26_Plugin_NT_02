// =======================================================
// File: FloorGeometryService.cs
// Location: Core/Services/
// V012: thin compatibility wrapper. The reading now lives in
// Infrastructure/Revit/FloorLoopReader (read-only, works on sloped and
// shape-edited floors). Kept so callers written for V011 — e.g.
// CombinedRoofTools — can switch to the V012 namespace without code
// changes. Note: V011 returned outer + inner loops and callers filtered
// on LoopType == "Inner"; V012 returns openings only, so that filter
// still gives the same set.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Models;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Infrastructure.Revit;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Services
{
    public class FloorGeometryService
    {
        /// <summary>Last read's notes (unclosed sketch loops, shape editing off …).</summary>
        public IReadOnlyList<string> LastNotes { get; private set; } = new List<string>();

        /// <summary>Openings of the floor (name kept from V011 — it was never circles only).</summary>
        public List<FloorLoopModel> ExtractCircularLoops(Floor floor)
        {
            FloorLoopReadResult read = FloorLoopReader.Read(floor);
            LastNotes = read.Notes;
            return read.InnerLoops;
        }
    }
}
