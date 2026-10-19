// IslandRoofSplitter V002 — the floor side of the output option (per Anas, 2026-10-15): same island footprint,
// created as a Floor instead of a FootPrintRoof. Used for Output = Floor, and as the fallback after every roof
// attempt failed.
//
// Revit 2026 API: Floor.Create(Document, IList<CurveLoop>, ElementId floorTypeId, ElementId levelId) — the
// NewFloor overloads on Document.Create are obsolete. Openings are extra CurveLoops after the outer loop.
// Height above level goes in FLOOR_HEIGHTABOVELEVEL_PARAM (the floor equivalent of ROOF_LEVEL_OFFSET_PARAM).
// Caller owns the Transaction.
#nullable enable
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class IslandFloorFactory
    {
        public static Floor Create(Document doc, Island island, Level level, FloorType type,
                                   double baseOffsetFt, string mark, bool tessellate, double shortCurveTolFt)
        {
            IList<CurveLoop> profile =
                ProfileBuilder.BuildCurveLoops(island, level.Elevation, tessellate, shortCurveTolFt);

            Floor floor = Floor.Create(doc, profile, type.Id, level.Id);

            floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(baseOffsetFt);
            var markParam = floor.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (markParam != null && !markParam.IsReadOnly) markParam.Set(mark);
            return floor;
        }
    }
}
