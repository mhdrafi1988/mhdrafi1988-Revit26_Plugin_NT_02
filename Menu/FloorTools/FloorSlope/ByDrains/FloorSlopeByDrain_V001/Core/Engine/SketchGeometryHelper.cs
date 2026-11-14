// File: SketchGeometryHelper.cs
// Location: Core/Engine/
// Ported from Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V67's
// FloorGeometry2D.cs — only the tessellation + polygon-area primitives that
// DrainDetectionService's Sketch-based opening detection actually needs.
// The Voronoi/ridge-line-specific members of the original file (PointInPolygon,
// segment intersection, nearest-drain-group lookup) are NOT ported — they
// belong to that tool's ridge pipeline and have no use here.

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine
{
    public static class SketchGeometryHelper
    {
        /// <summary>
        /// Tessellates a closed sketch-profile loop (lines kept as endpoints, curves
        /// tessellated) and flattens it to Z=0, de-duplicating adjacent points within
        /// snap tolerance.
        /// </summary>
        public static List<XYZ> TessellateLoop(CurveArray loop)
        {
            var pts = new List<XYZ>();
            foreach (Curve c in loop) AppendCurve(c, pts);
            return Flatten(pts);
        }

        private static void AppendCurve(Curve c, List<XYZ> pts)
        {
            if (c is Line)
                pts.Add(c.GetEndPoint(0));
            else
            {
                IList<XYZ> tess = c.Tessellate();
                for (int i = 0; i < tess.Count - 1; i++) pts.Add(tess[i]);
            }
        }

        private static List<XYZ> Flatten(List<XYZ> pts)
        {
            const double snapTol = 1e-6;
            var flat = new List<XYZ>(pts.Count);
            XYZ prev = null;
            foreach (var p in pts)
            {
                var fp = new XYZ(p.X, p.Y, 0);
                if (prev != null && fp.DistanceTo(prev) < snapTol) continue;
                flat.Add(fp);
                prev = fp;
            }
            return flat;
        }

        /// <summary>
        /// Approximate planar area of a CurveArray loop (Shoelace formula), using
        /// the SAME arc tessellation as TessellateLoop.
        ///
        /// FIX: previously sampled only each curve's start endpoint (one point per
        /// curve, ignoring arc curvature entirely). For a circle represented as just
        /// 2-3 Arc segments — Revit's typical representation of a sketched circle,
        /// since a full 360° arc can't be a single bounded Curve — this collapsed
        /// the "polygon" to 2-3 points, which the Shoelace formula measures as
        /// near-zero area regardless of the circle's actual size. When the true
        /// outer boundary was itself a circle, this could make its approximated
        /// area come out SMALLER than an inner opening's, causing
        /// ExtractOpeningLoopsFromSketch's Max(area) outer/inner split to pick the
        /// wrong loop as the boundary. Using full tessellation (which walks along
        /// each arc's real curvature, not just its endpoints) measures every loop's
        /// true footprint correctly, circular or not, whether it's the outer
        /// boundary or an inner opening.
        /// </summary>
        public static double ApproximateLoopArea(CurveArray loop)
        {
            return ApproximatePolygonArea(TessellateLoop(loop));
        }

        /// <summary>Shoelace-formula area of a flattened (Z=0) polygon point list.</summary>
        public static double ApproximatePolygonArea(List<XYZ> pts)
        {
            int n = pts.Count;
            if (n < 3) return 0;
            double area = 0;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                area += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(area) / 2.0;
        }

        /// <summary>2D (XY-plane) distance between two points; Z is ignored. Used by the circle-classification heuristic.</summary>
        public static double Dist2D(XYZ a, XYZ b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
