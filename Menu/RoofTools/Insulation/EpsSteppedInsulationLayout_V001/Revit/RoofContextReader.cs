using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit
{
    /// <summary>Reads the roof outline, shape editing vertices and drain points into an <see cref="InsulationRoofContext"/>.</summary>
    public static class RoofContextReader
    {
        public static InsulationRoofContext Build(Document doc, ElementId viewId, ElementId roofId)
        {
            var view = doc.GetElement(viewId) as ViewPlan;
            var roof = doc.GetElement(roofId) as FootPrintRoof;

            if (view == null || roof == null)
                return Error(doc, viewId, roofId, "The roof or view no longer exists.");

            Level level = view.GenLevel;

            // Read outline from profiles
            IReadOnlyList<Vec2> outline = ReadOutline(roof);
            if (outline == null || outline.Count < 3)
                return Error(doc, viewId, roofId, "Could not read the roof outline (fewer than 3 edges).");

            // Read shape editing vertices
            var (shapeVerts, drains) = ReadShapeVertices(roof);

            double baseOffset = 0;
            try
            {
                Parameter offsetParam = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);
                if (offsetParam != null) baseOffset = offsetParam.AsDouble();
            }
            catch { /* ignore */ }

            return new InsulationRoofContext(doc, viewId, roofId)
            {
                ViewName = view.Name,
                RoofName = Describe(roof),
                LevelName = level?.Name ?? "(no level)",
                LevelId = level?.Id ?? ElementId.InvalidElementId,
                LevelElevationFeet = level?.ProjectElevation ?? 0.0,
                BaseOffsetFeet = baseOffset,
                OutlineMetres = outline,
                Drains = drains,
                ShapeVertices = shapeVerts
            };
        }

        /// <summary>Reads the outer boundary loop from the roof's sketch profile.</summary>
        private static IReadOnlyList<Vec2> ReadOutline(FootPrintRoof roof)
        {
            ModelCurveArrArray profiles = roof.GetProfiles();
            if (profiles == null || profiles.Size == 0) return null;

            // Use the first (outer) profile
            ModelCurveArray outer = profiles.get_Item(0);
            var points = new List<Vec2>();
            foreach (ModelCurve mc in outer)
            {
                Curve c = mc.GeometryCurve;
                XYZ start = c.GetEndPoint(0);
                points.Add(ToPlanMetres(start));
            }

            if (points.Count < 3) return null;

            // Ensure CCW
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Vec2 a = points[i];
                Vec2 b = points[(i + 1) % points.Count];
                area += (b.X - a.X) * (b.Y + a.Y);
            }
            if (area > 0) points.Reverse();

            return points;
        }

        /// <summary>
        /// Reads shape editing vertices and identifies drains (lowest-elevation vertices).
        /// </summary>
        private static (IReadOnlyList<(Vec2, double)> All, IReadOnlyList<DrainPoint> Drains) ReadShapeVertices(FootPrintRoof roof)
        {
            SlabShapeEditor editor = roof.GetSlabShapeEditor();
            if (editor == null || !editor.IsValidObject)
                return (Array.Empty<(Vec2, double)>(), Array.Empty<DrainPoint>());

            var all = new List<(Vec2 Plan, double ElevationFeet)>();
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
            {
                XYZ pos = vertex.Position;
                all.Add((ToPlanMetres(pos), pos.Z));
            }

            if (all.Count == 0)
                return (all, Array.Empty<DrainPoint>());

            // Drains = vertices at the lowest elevation (within 1 mm tolerance)
            double minZ = all.Min(v => v.ElevationFeet);
            double toleranceFeet = MetresToFeet(0.001);

            var drains = all
                .Where(v => v.ElevationFeet <= minZ + toleranceFeet)
                .Select(v => new DrainPoint(v.Plan, v.ElevationFeet))
                .ToList();

            return (all, drains);
        }

        private static string Describe(RoofBase roof)
        {
            string type = roof.Document.GetElement(roof.GetTypeId())?.Name ?? "Roof";
            return $"{type}  [Id {roof.Id.Value}]";
        }

        private static Vec2 ToPlanMetres(XYZ point) =>
            new Vec2(FeetToMetres(point.X), FeetToMetres(point.Y));

        private static double FeetToMetres(double feet) =>
            UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);

        private static double MetresToFeet(double metres) =>
            UnitUtils.ConvertToInternalUnits(metres, UnitTypeId.Meters);

        private static InsulationRoofContext Error(Document doc, ElementId viewId, ElementId roofId, string msg) =>
            new InsulationRoofContext(doc, viewId, roofId) { Error = msg };
    }
}
