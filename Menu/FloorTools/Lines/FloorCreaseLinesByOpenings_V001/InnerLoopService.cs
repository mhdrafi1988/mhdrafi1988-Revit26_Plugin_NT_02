using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Services
{
    /// <summary>
    /// Extracts all interior void/opening loops from a <see cref="Floor"/> element.
    /// Uses a two-pass strategy:
    /// <list type="bullet">
    ///   <item>Pass 1 — Dependent <see cref="Sketch"/> element (Floor, Revit 2025+).
    ///   Preferred: the sketch profile contains exactly one loop per drawn polygon with no
    ///   geometry duplication.</item>
    ///   <item>Pass 2 — Geometry solid top face (Floor or sketch-less floors).
    ///   Fallback: reads edge loops from the single highest-Z horizontal face only, so
    ///   bottom-face loops are never included.</item>
    /// </list>
    /// The largest loop by area is always the outer boundary; every other loop is an
    /// interior opening. All vertices are flattened to Z = 0 and tessellated.
    /// No transaction required — read-only API.
    /// </summary>
    public class InnerLoopService
    {
        /// <summary>
        /// Returns every interior void/opening loop on <paramref name="floor"/>
        /// (Z = 0, tessellated), excluding the outer boundary loop itself.
        /// </summary>
        /// <param name="floor">The floor element to inspect.</param>
        /// <returns>
        /// A list of vertex lists, one per opening. Empty list if the floor has no openings.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="floor"/> is null.</exception>
        public List<List<XYZ>> ExtractInnerLoops(Floor floor)
        {
            if (floor == null) throw new ArgumentNullException(nameof(floor));

            var result = TryExtractFromSketch(floor);
            if (result != null)
                return result;

            return ExtractFromTopFace(floor);
        }

        // ── Pass 1: Sketch element (Floor, Revit 2025+) ──────────────────

        /// <summary>
        /// Attempts to extract inner loops from the floor's dependent <see cref="Sketch"/>.
        /// Returns the inner loops on success, an empty list if there are no openings,
        /// or <c>null</c> if no sketch was found (triggers the geometry fallback).
        /// </summary>
        private List<List<XYZ>> TryExtractFromSketch(Floor floor)
        {
            try
            {
                var doc = floor.Document;
                var dependentIds = floor.GetDependentElements(new ElementClassFilter(typeof(Sketch)));

                foreach (ElementId id in dependentIds)
                {
                    if (!(doc.GetElement(id) is Sketch sketch)) continue;

                    var loops = new List<(CurveArray loop, double area)>();
                    foreach (CurveArray loop in sketch.Profile)
                    {
                        double area = FloorGeometry2D.ApproximateLoopArea(loop);
                        loops.Add((loop, area));
                    }

                    // Only one loop = boundary only, no openings.
                    if (loops.Count <= 1) return new List<List<XYZ>>();

                    // Largest loop = outer boundary; everything else = openings.
                    double maxArea = loops.Max(l => l.area);
                    return loops
                        .Where(l => l.area < maxArea)
                        .Select(l => FloorGeometry2D.TessellateLoop(l.loop))
                        .Where(pts => pts.Count >= 3)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[InnerLoopService] Sketch pass failed, falling back to geometry: {ex.Message}");
            }

            // No sketch found — caller will use the geometry fallback.
            return null;
        }

        // ── Pass 2: Geometry solid — top face only ────────────────────────────────

        /// <summary>
        /// Fallback for floors without a sketch (e.g. Floor).
        /// Reads edge loops from the single top-most horizontal face of each solid only.
        /// Bottom-face loops are intentionally excluded: a slab solid carries identical
        /// boundary + opening loops on both faces, and reading both would duplicate every
        /// opening once Z is flattened to 0 downstream.
        /// </summary>
        private List<List<XYZ>> ExtractFromTopFace(Floor floor)
        {
            var opts = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            var allLoops = new List<(List<XYZ> pts, double area)>();

            foreach (GeometryObject obj in floor.get_Geometry(opts))
            {
                if (!(obj is Solid solid) || solid.Volume < 1e-6) continue;

                // Pick the single top-most horizontal face (highest average Z).
                Face topFace = null;
                double bestZ = double.NegativeInfinity;

                foreach (Face face in solid.Faces)
                {
                    XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                    if (Math.Abs(normal.Z) < 0.3) continue; // skip vertical faces

                    double avgZ = AverageFaceZ(face);
                    if (avgZ > bestZ)
                    {
                        bestZ = avgZ;
                        topFace = face;
                    }
                }

                if (topFace == null) continue;

                foreach (EdgeArray loop in topFace.EdgeLoops)
                {
                    var pts = FloorGeometry2D.TessellateEdgeLoop(loop);
                    double area = FloorGeometry2D.ApproximatePolygonArea(pts);
                    allLoops.Add((pts, area));
                }
            }

            if (allLoops.Count <= 1) return new List<List<XYZ>>();

            double maxArea = allLoops.Max(l => l.area);
            return allLoops
                .Where(l => l.area < maxArea && l.pts.Count >= 3)
                .Select(l => l.pts)
                .ToList();
        }

        /// <summary>
        /// Returns the average Z of all tessellated edge vertices on a face.
        /// Used to identify the top-most horizontal face of a solid.
        /// </summary>
        private static double AverageFaceZ(Face face)
        {
            double sum = 0.0;
            int count = 0;
            foreach (EdgeArray loop in face.EdgeLoops)
                foreach (Edge edge in loop)
                    foreach (XYZ p in edge.Tessellate())
                    { sum += p.Z; count++; }

            return count > 0 ? sum / count : double.NegativeInfinity;
        }
    }
}
