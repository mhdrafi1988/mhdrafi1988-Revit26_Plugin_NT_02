using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTag.V001.Helpers
{
    internal static partial class FloorTagGeometryHelper
    {
        public static List<XYZ> GetExactShapeVertices(Floor floor)
        {
            if (floor == null)
                throw new ArgumentNullException(nameof(floor));

            SlabShapeEditor editor = floor.GetSlabShapeEditor();
            if (editor == null || !editor.IsEnabled)
                return new List<XYZ>();

            const double tol = 1e-6;

            return editor.SlabShapeVertices
                .Cast<SlabShapeVertex>()
                .GroupBy(v => (
                    Math.Round(v.Position.X / tol),
                    Math.Round(v.Position.Y / tol)))
                .Select(g => g.OrderByDescending(v => v.Position.Z).First().Position)
                .ToList();
        }

        public static List<XYZ> BuildFloorBoundaryXY(Floor floor)
        {
            List<XYZ> pts = new();
            Options opt = new();
            GeometryElement geo = floor.get_Geometry(opt);

            foreach (GeometryObject obj in geo)
            {
                if (obj is Solid solid)
                {
                    foreach (Edge e in solid.Edges)
                    {
                        Curve c = e.AsCurve();
                        pts.Add(new XYZ(c.GetEndPoint(0).X, c.GetEndPoint(0).Y, 0));
                        pts.Add(new XYZ(c.GetEndPoint(1).X, c.GetEndPoint(1).Y, 0));
                    }
                }
            }
            return pts;
        }
    }
}
