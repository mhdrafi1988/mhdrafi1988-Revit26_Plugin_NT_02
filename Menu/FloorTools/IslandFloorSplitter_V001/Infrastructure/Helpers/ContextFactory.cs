// Island Floor Splitter V001 (floor port of Island Roof Splitter V002) — builds the Revit-free SplitterContext
// from a picked FLOOR + detail curves. Field names (RoofId, RoofTitle, …) are kept from the roof tool and now
// describe the source floor. Output is always floors, so RoofTypes is left empty and the floor type defaults
// to the source floor's type.
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
using Revit26_Plugin_Revised.IslandFloorSplitter.V001.Core.Models;

namespace Revit26_Plugin_Revised.IslandFloorSplitter.V001.Infrastructure.Helpers
{
    public static class ContextFactory
    {
        /// <summary>Snapshot of the source floor and the picked detail curves.</summary>
        public static SplitterContext Build(UIDocument uidoc, Floor roof, List<DetailCurve> curves)
        {
            Document doc = uidoc.Document;
            var segments = DetailCurveExtractor.Extract(curves, out int nLines, out int nArcs, out int nOther);
            var mesh = RoofSurfaceExtractor.Extract(roof);

            var level = doc.GetElement(roof.LevelId) as Level;
            double offset = roof.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
            var type = doc.GetElement(roof.GetTypeId()) as FloorType;

            // Floors have no slope-defining edges; report how many shape points the floor has instead.
            SlabShapeEditor? editor = roof.GetSlabShapeEditor();
            int shapePoints = editor != null && editor.IsEnabled ? editor.SlabShapeVertices.Size : 0;

            var parts = new List<string>();
            if (nLines > 0) parts.Add($"{nLines} straight");
            if (nArcs > 0) parts.Add($"{nArcs} arc{(nArcs == 1 ? "" : "s")}");
            if (nOther > 0) parts.Add($"{nOther} other");

            return new SplitterContext
            {
                RoofId = roof.Id.Value,
                RoofTitle = $"{type?.FamilyName ?? "Floor"} : {type?.Name ?? roof.Name}",
                RoofDetail = $"ID {roof.Id.Value} · " +
                             (level != null
                                 ? $"{level.Name} ({level.Elevation * LengthUnits.MmPerFoot:0} mm)"
                                 : "level not detected") +
                             $" · height offset {offset * LengthUnits.MmPerFoot:0} mm · " +
                             (shapePoints > 0 ? $"{shapePoints} shape point{(shapePoints == 1 ? "" : "s")}" : "flat (shape editing off)") +
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
                RoofTypes = new List<NamedId>(), // floor port: no roof output
                Levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new LevelOption(l.Id.Value, l.Name, l.Elevation)).ToList(),
                FloorTypes = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                    .Where(ft => !ft.IsFoundationSlab)
                    .OrderBy(ft => ft.FamilyName).ThenBy(ft => ft.Name)
                    .Select(ft => new NamedId(ft.Id.Value, $"{ft.FamilyName} : {ft.Name}")).ToList(),
                // Floor port: new floors default to the source floor's type, else the document default.
                DefaultFloorTypeId = type != null ? type.Id.Value : DefaultFloorTypeId(doc),
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
        private static double? TopZ(Floor roof, RoofSurfaceMesh mesh)
        {
            if (mesh.Triangles.Count > 0)
                return mesh.Triangles.Min(t => System.Math.Min(t.A.Z, System.Math.Min(t.B.Z, t.C.Z)));

            BoundingBoxXYZ? bb = roof.get_BoundingBox(null);
            return bb?.Min.Z;
        }
    }
}
