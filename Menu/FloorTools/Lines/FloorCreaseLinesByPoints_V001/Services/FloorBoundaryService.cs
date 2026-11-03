using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Services
{
    /// <summary>
    /// Extracts the 2D plan boundary polygon from a Floor element.
    /// Revit 2026: GetSketch() does not exist on Floor.
    /// Strategy: parse the floor geometry solid, project bottom face edges to Z=0.
    /// Arcs and curves are tessellated so downstream clipping treats the result as a polygon.
    /// No transaction required — read-only API.
    /// </summary>
    public class FloorBoundaryService
    {
        /// <summary>
        /// Extracts the outer boundary polygon of <paramref name="floor"/> (Z=0, tessellated).
        /// Tries the floor's sketch profile first, falling back to the solid geometry's
        /// bottom face if no sketch is found.
        /// </summary>
        public List<XYZ> ExtractBoundary(Floor floor)
        {
            if (floor == null) throw new ArgumentNullException(nameof(floor));

            // ── Try sketch via SketchBase filter (Revit 2026 approach) ─────────────
            var sketchResult = TryExtractFromSketchElement(floor);
            if (sketchResult != null && sketchResult.Count >= 3)
                return sketchResult;

            // ── Fallback: geometry solid bottom face ──────────────────────────────
            return ExtractFromGeometry(floor);
        }

        // ── Sketch via document filter ────────────────────────────────────────────

        private List<XYZ> TryExtractFromSketchElement(Floor floor)
        {
            try
            {
                // In Revit 2025+, the sketch is a separate Sketch element owned by the floor.
                // We can find it via the floor's dependent elements.
                var doc = floor.Document;
                var dependentIds = floor.GetDependentElements(new ElementClassFilter(typeof(Sketch)));

                foreach (ElementId id in dependentIds)
                {
                    if (doc.GetElement(id) is Sketch sketch)
                    {
                        var loop = GetLargestLoop(sketch.Profile);
                        if (loop != null) return FloorGeometry2D.TessellateLoop(loop);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FloorBoundaryService] Sketch extraction skipped: {ex.Message}");
            }
            return null;
        }

        private static CurveArray GetLargestLoop(CurveArrArray profile)
        {
            CurveArray best = null;
            double maxArea = -1;
            foreach (CurveArray loop in profile)
            {
                double area = FloorGeometry2D.ApproximateLoopArea(loop);
                if (area > maxArea) { maxArea = area; best = loop; }
            }
            return best;
        }

        // ── Geometry fallback ─────────────────────────────────────────────────────

        private List<XYZ> ExtractFromGeometry(Floor floor)
        {
            var opts = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            List<XYZ> bestLoop = null;
            double maxArea = -1;

            foreach (GeometryObject obj in floor.get_Geometry(opts))
            {
                if (!(obj is Solid solid) || solid.Volume < 1e-6) continue;

                foreach (Face face in solid.Faces)
                {
                    // Use the largest horizontal-ish face (floor footprint)
                    XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                    if (Math.Abs(normal.Z) < 0.3) continue; // skip vertical faces

                    foreach (EdgeArray loop in face.EdgeLoops)
                    {
                        var pts = FloorGeometry2D.TessellateEdgeLoop(loop);
                        var area = FloorGeometry2D.ApproximatePolygonArea(pts);
                        if (area > maxArea) { maxArea = area; bestLoop = pts; }
                    }
                }
            }

            if (bestLoop == null || bestLoop.Count < 3)
                throw new InvalidOperationException(
                    "Could not extract floor boundary. Ensure the floor has a valid 3D solid geometry.");

            return bestLoop;
        }
    }
}
