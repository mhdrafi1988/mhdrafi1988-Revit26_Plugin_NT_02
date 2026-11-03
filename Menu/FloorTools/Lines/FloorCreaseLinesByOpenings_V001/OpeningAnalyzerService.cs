using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Services
{
    /// <summary>
    /// Static helper that analyses an inner‑loop vertex list and produces an <see cref="OpeningData"/>.
    /// Uses simple heuristics to detect Rectangle / Circle / Other.
    /// Centroid = bounding‑box centre (as specified).
    /// </summary>
    public static class OpeningAnalyzerService
    {
        /// <summary>
        /// Analyses a closed loop of 2D points (Z ignored).
        /// </summary>
        /// <param name="vertices">Flattened loop vertices (Z=0).</param>
        /// <returns>An <see cref="OpeningData"/> with shape type, dimensions, BBox, centroid.</returns>
        /// <exception cref="ArgumentException">If fewer than 3 vertices.</exception>
        public static OpeningData AnalyzeLoop(List<XYZ> vertices)
        {
            if (vertices == null || vertices.Count < 3)
                throw new ArgumentException("At least 3 vertices required.", nameof(vertices));

            var pts = vertices.Select(v => new XYZ(v.X, v.Y, 0)).ToList();

            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double width = maxX - minX;
            double height = maxY - minY;

            // Centroid = BBox centre (as requested in Q2 – Option C)
            var centroid = new XYZ((minX + maxX) / 2.0, (minY + maxY) / 2.0, 0);

            OpeningShapeType shapeType;
            double dim1 = width, dim2 = height;

            // ── Heuristic: Rectangle ──
            // Exactly 4 vertices and consecutive edges are perpendicular.
            if (pts.Count == 4)
            {
                bool isRect = true;
                for (int i = 0; i < 4; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % 4];
                    var c = pts[(i + 2) % 4];
                    XYZ e1 = new XYZ(b.X - a.X, b.Y - a.Y, 0);
                    XYZ e2 = new XYZ(c.X - b.X, c.Y - b.Y, 0);
                    double dot = e1.X * e2.X + e1.Y * e2.Y;
                    if (Math.Abs(dot) > 1e-6) { isRect = false; break; }
                }
                if (isRect)
                {
                    shapeType = OpeningShapeType.Rectangle;
                    dim1 = width;
                    dim2 = height;
                }
                else shapeType = OpeningShapeType.Other;
            }
            // ── Heuristic: Circle ──
            // At least 12 vertices and all distances from centroid are within 5% of average.
            else if (pts.Count >= 12)
            {
                double avgDist = pts.Average(p => FloorGeometry2D.Dist2D(p, centroid));
                if (avgDist > 1e-9)
                {
                    double stdDev = Math.Sqrt(pts.Average(p =>
                        Math.Pow(FloorGeometry2D.Dist2D(p, centroid) - avgDist, 2)));
                    if (stdDev / avgDist < 0.05) // within 5%
                    {
                        shapeType = OpeningShapeType.Circle;
                        dim1 = avgDist;          // radius
                        dim2 = 0;
                    }
                    else shapeType = OpeningShapeType.Other;
                }
                else shapeType = OpeningShapeType.Other;
            }
            else
            {
                shapeType = OpeningShapeType.Other;
            }

            return new OpeningData
            {
                Vertices = pts,
                ShapeType = shapeType,
                Dim1 = dim1,
                Dim2 = dim2,
                BBoxWidth = width,
                BBoxHeight = height,
                Centroid = centroid,
                IsSelected = false
            };
        }
    }
}