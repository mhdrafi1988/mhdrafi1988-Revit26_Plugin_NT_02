// IslandRoofSplitter V002 — creates one footprint roof per island (no slope-defining edges), sets type, level,
// base offset and Mark. Openings (Ring mode) are passed as extra closed loops in the same footprint CurveArray.
// Caller owns the Transaction.
//
// V002: the footprint now comes from ProfileBuilder (sanitised, optionally arc-tessellated), so the handler can
// retry a failed island with tessellate: true before falling back to a floor.
#nullable enable
using Autodesk.Revit.DB;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class IslandRoofFactory
    {
        public static FootPrintRoof Create(Document doc, Island island, Level level, RoofType type,
                                           double baseOffsetFt, string mark, bool tessellate, double shortCurveTolFt)
        {
            var footprint = ProfileBuilder.BuildCurveArray(island, level.Elevation, tessellate, shortCurveTolFt);

            FootPrintRoof roof = doc.Create.NewFootPrintRoof(footprint, level, type, out ModelCurveArray map);
            foreach (ModelCurve mc in map)
                roof.set_DefinesSlope(mc, false);

            roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM)?.Set(baseOffsetFt);
            var markParam = roof.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (markParam != null && !markParam.IsReadOnly) markParam.Set(mark);
            return roof;
        }
    }
}
