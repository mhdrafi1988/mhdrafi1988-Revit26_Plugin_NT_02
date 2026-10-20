// IslandRoofSplitter V002 — builds the Revit-free SplitterContext from a picked roof + detail curves.
// Used by the Command and by the handler's Re-pick requests.
//
// Update (2026-10-15, per Anas): the snapshot now reports whether the level and roof type were actually
// detected on the source roof instead of letting the UI fall back silently, carries each level's elevation
// (LevelOption) and a reference Z (RoofTopZFt) for the nearest-level pre-selection.
// V002: also captures the floor types (foundation slabs excluded) and the document's default floor type,
// for the roof → floor fallback.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class ContextFactory
    {
        public static SplitterContext Build(UIDocument uidoc, FootPrintRoof roof, List<DetailCurve> curves)
        {
            Document doc = uidoc.Document;
            var segments = DetailCurveExtractor.Extract(curves, out int nLines, out int nArcs, out int nOther);
            var mesh = RoofSurfaceExtractor.Extract(roof);

            var level = doc.GetElement(roof.LevelId) as Level;
            double offset = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;
            var type = doc.GetElement(roof.GetTypeId()) as RoofType;

            int slopeEdges = 0;
            foreach (ModelCurveArray loop in roof.GetProfiles())
                foreach (ModelCurve mc in loop)
                    if (roof.get_DefinesSlope(mc)) slopeEdges++;

            var parts = new List<string>();
            if (nLines > 0) parts.Add($"{nLines} straight");
            if (nArcs > 0) parts.Add($"{nArcs} arc{(nArcs == 1 ? "" : "s")}");
            if (nOther > 0) parts.Add($"{nOther} other");

            return new SplitterContext
            {
                RoofId = roof.Id.Value,
                RoofTitle = $"{type?.FamilyName ?? "Roof"} : {type?.Name ?? roof.Name}",
                RoofDetail = $"ID {roof.Id.Value} · " +
                             (level != null
                                 ? $"{level.Name} ({level.Elevation * LengthUnits.MmPerFoot:0} mm)"
                                 : "level not detected") +
                             $" · base offset {offset * LengthUnits.MmPerFoot:0} mm · " +
                             $"{slopeEdges} slope-defining edge{(slopeEdges == 1 ? "" : "s")}" +
                             (mesh.Triangles.Count == 0 ? " · no top faces found" : ""),
                RoofTypeId = roof.GetTypeId().Value,
                LevelId = roof.LevelId.Value,
                LevelDetected = level != null,
                RoofTypeDetected = type != null,
                RoofTopZFt = TopZ(roof, mesh),
                BaseOffsetFt = offset,
                Mesh = mesh,
                Segments = segments,
                LinesTitle = $"{curves.Count} detail curve{(curves.Count == 1 ? "" : "s")} ({string.Join(" · ", parts)})",
                LinesDetail = $"View: {uidoc.ActiveView.Name}",
                RoofTypes = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>()
                    .OrderBy(t => t.FamilyName).ThenBy(t => t.Name)
                    .Select(t => new NamedId(t.Id.Value, $"{t.FamilyName} : {t.Name}")).ToList(),
                Levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new LevelOption(l.Id.Value, l.Name, l.Elevation)).ToList(),
                FloorTypes = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                    .Where(ft => !ft.IsFoundationSlab)
                    .OrderBy(ft => ft.FamilyName).ThenBy(ft => ft.Name)
                    .Select(ft => new NamedId(ft.Id.Value, $"{ft.FamilyName} : {ft.Name}")).ToList(),
                DefaultFloorTypeId = DefaultFloorTypeId(doc),
                ShortCurveTolFt = doc.Application.ShortCurveTolerance
            };
        }

        /// <summary>The document's default floor type, or 0 when it has none.</summary>
        private static long DefaultFloorTypeId(Document doc)
        {
            ElementId id = doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType);
            return id != null && id != ElementId.InvalidElementId ? id.Value : 0;
        }

        /// <summary>Lowest Z of the captured top faces; the roof's bounding box when there are none.</summary>
        private static double? TopZ(FootPrintRoof roof, RoofSurfaceMesh mesh)
        {
            if (mesh.Triangles.Count > 0)
                return mesh.Triangles.Min(t => System.Math.Min(t.A.Z, System.Math.Min(t.B.Z, t.C.Z)));

            BoundingBoxXYZ? bb = roof.get_BoundingBox(null);
            return bb?.Min.Z;
        }
    }
}
