// =======================================================
// File: FloorGeometryService.cs
// Location: Core/Services/
// Ported unchanged from V003 (namespace updated only).
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Services
{
    /// <summary>
    /// Extracts boundary loops from a floor and classifies them by shape
    /// (Circular, Rectangular, Other).
    /// </summary>
    public class FloorGeometryService
    {
        /// <summary>
        /// Extracts all boundary loops from the given floor, classifies them
        /// by shape, and returns them as a list of <see cref="FloorLoopModel"/>.
        /// </summary>
        /// <param name="floor">The floor from which to extract loops.</param>
        /// <returns>A list of <see cref="FloorLoopModel"/> representing the floor's boundaries.</returns>
        public List<FloorLoopModel> ExtractCircularLoops(Floor floor)
        {
            var result = new List<FloorLoopModel>();
            Options opt = new Options { ComputeReferences = true };
            GeometryElement geoElem = floor.get_Geometry(opt);

            foreach (GeometryObject obj in geoElem)
            {
                if (obj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf && pf.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ))
                        {
                            var loops = pf.GetEdgesAsCurveLoops();
                            int idx = 0;

                            var loopInfos = loops.Select(cl => new
                            {
                                Index = ++idx,
                                CurveLoop = cl,
                                Perimeter = cl.Sum(c => c.Length),
                                LoopShapeType = ClassifyLoopShape(cl, out XYZ center, out double radius)
                            }).ToList();

                            double maxPerimeter = loopInfos.Max(l => l.Perimeter);

                            foreach (var loopInfo in loopInfos)
                            {
                                string loopType = Math.Abs(loopInfo.Perimeter - maxPerimeter) < 1e-6
                                    ? "Outer"
                                    : "Inner";

                                result.Add(new FloorLoopModel
                                {
                                    Index = loopInfo.Index,
                                    PerimeterMm = UnitUtils.ConvertFromInternalUnits(loopInfo.Perimeter, UnitTypeId.Millimeters),
                                    LoopType = loopType,
                                    LoopShapeType = loopInfo.LoopShapeType,
                                    Geometry = loopInfo.CurveLoop
                                });
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Classifies a curve loop as Circular, Rectangular, or Other based on
        /// its geometry.
        /// </summary>
        /// <param name="loop">The curve loop to classify.</param>
        /// <param name="center">Output: The center point if circular, otherwise zero.</param>
        /// <param name="radius">Output: The radius if circular, otherwise zero.</param>
        /// <returns>The shape type: "Circular", "Rectangle", or "Other".</returns>
        private string ClassifyLoopShape(CurveLoop loop, out XYZ center, out double radius)
        {
            center = XYZ.Zero;
            radius = 0;

            var curves = loop.ToList();

            // CIRCULAR: All arcs with same center and radius, 360° sweep
            var arcs = curves.OfType<Arc>().ToList();
            if (arcs.Count == curves.Count && arcs.Count >= 3)
            {
                var centers = arcs.Select(a => a.Center).ToList();
                var avgCenter = new XYZ(
                    centers.Average(p => p.X),
                    centers.Average(p => p.Y),
                    centers.Average(p => p.Z));

                bool centersClose = centers.All(c => c.DistanceTo(avgCenter) < 0.1);
                if (!centersClose) return "Other";

                var radii = arcs.Select(a => a.Radius).ToList();
                double avgRadius = radii.Average();
                bool radiusClose = radii.All(r => Math.Abs(r - avgRadius) / avgRadius < 0.01);
                if (!radiusClose) return "Other";

                double totalAngle = arcs.Sum(a =>
                {
                    XYZ arcCenter = a.Center;
                    XYZ startVec = (a.GetEndPoint(0) - arcCenter).Normalize();
                    XYZ endVec = (a.GetEndPoint(1) - arcCenter).Normalize();
                    return startVec.AngleTo(endVec);
                });

                if (Math.Abs(totalAngle - 2 * Math.PI) > 0.05)
                    return "Other";

                center = avgCenter;
                radius = avgRadius;
                return "Circular";
            }

            // RECTANGLE: 4 straight lines, 90-degree corners
            var lines = curves.OfType<Line>().ToList();
            if (lines.Count == 4 && curves.Count == 4)
            {
                var angles = new List<double>();
                for (int i = 0; i < 4; i++)
                {
                    var a = lines[i];
                    var b = lines[(i + 1) % 4];

                    XYZ v1 = (a.GetEndPoint(1) - a.GetEndPoint(0)).Normalize();
                    XYZ v2 = (b.GetEndPoint(1) - b.GetEndPoint(0)).Normalize();
                    double angle = v1.AngleTo(v2);
                    angles.Add(angle);
                }

                if (angles.All(a => Math.Abs(a - Math.PI / 2) < 0.1))
                    return "Rectangle";
            }

            return "Other";
        }
    }
}
