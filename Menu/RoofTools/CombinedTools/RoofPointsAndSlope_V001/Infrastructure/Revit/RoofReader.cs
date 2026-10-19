// =======================================================
// File: RoofReader.cs
// Location: Infrastructure/Revit/
// Reads a footprint roof into a RoofOutline (read-only, no transaction).
// Opening detection follows Inner Loop Divider V012's RoofLoopReader:
//   1. Sketch profiles: a loop inside an odd number of bigger loops is an
//      opening, every other loop is a boundary (separate islands are
//      boundaries, not openings).
//   2. Holes in the top faces — catches opening cuts that are not in the
//      sketch. Duplicates of sketch openings are merged.
// Unlike V012, every sketch curve stays ONE edge (ellipses and splines
// are not split into pieces), because steps 3 and 4 work per edge.
// Openings are numbered north to south, then west to east, and matched
// to the openings Auto Slope By Drain V013 detects, so they can be used
// as drains.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Planning;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.Revit
{
    /// <summary>Reads a footprint roof's boundaries, openings, edges and shape points.</summary>
    public static class RoofReader
    {
        private const double Tolerance = PointPipelinePlanner.Tolerance;

        /// <summary>
        /// Reads <paramref name="roof"/>. <paramref name="drains"/> (from Auto Slope By Drain V013's
        /// detection, may be null) are matched to the openings found.
        /// </summary>
        public static RoofOutline Read(FootPrintRoof roof, IReadOnlyList<DrainItem> drains)
        {
            if (roof == null) throw new ArgumentNullException(nameof(roof));
            var outline = new RoofOutline();
            var openings = new List<(Loop2D Loop, string Source)>();

            // 1 — sketch profiles
            var sketchLoops = new List<Loop2D>();
            int n = 0;
            foreach (ModelCurveArray profile in roof.GetProfiles())
            {
                n++;
                var segs = new List<LoopSegment2D>();
                foreach (ModelCurve mc in profile)
                {
                    LoopSegment2D seg = CurveConverter.ToSegment(mc.GeometryCurve);
                    if (seg != null) segs.Add(seg);
                }

                Loop2D loop = LoopChainer.Chain(segs, Tolerance);
                if (loop == null)
                    outline.Notes.Add($"Sketch loop {n} is not closed — skipped.");
                else
                    sketchLoops.Add(loop);
            }

            foreach (Loop2D l in sketchLoops)
            {
                int depth = sketchLoops.Count(o => !ReferenceEquals(o, l) && o.Area > l.Area && o.Contains(l.Samples[0]));
                if (depth % 2 == 0)
                    outline.Boundaries.Add(l);
                else
                    openings.Add((l, "Sketch"));
            }

            if (outline.Boundaries.Count == 0)
                outline.Notes.Add("No closed boundary loop was found in the roof's sketch.");

            // 2 — holes in the top faces
            foreach (Reference r in HostObjectUtils.GetTopFaces(roof))
            {
                if (roof.GetGeometryObjectFromReference(r) is not Face face) continue;

                var faceLoops = new List<Loop2D>();
                foreach (CurveLoop cl in face.GetEdgesAsCurveLoops())
                {
                    var segs = cl.Select(CurveConverter.ToSegment).Where(s => s != null).ToList();
                    Loop2D loop = LoopChainer.Chain(segs, Tolerance);
                    if (loop != null && loop.Area > Tolerance * Tolerance)
                        faceLoops.Add(loop);
                }

                if (faceLoops.Count < 2) continue;
                Loop2D outerFace = faceLoops.OrderByDescending(l => l.Area).First();
                int outerSign = Math.Sign(outerFace.SignedArea);
                foreach (Loop2D fl in faceLoops)
                {
                    if (ReferenceEquals(fl, outerFace) || Math.Sign(fl.SignedArea) == outerSign) continue;
                    if (openings.Any(o => SameLoop(o.Loop, fl))) continue;
                    if (!outline.Boundaries.Any(b => b.Contains(fl.Centroid))) continue; // a hole of the face, not of the roof
                    openings.Add((fl, "Opening cut"));
                }
            }

            // 3 — edges and numbered openings
            foreach (Loop2D b in outline.Boundaries)
                foreach (LoopSegment2D seg in b.Segments)
                    outline.Edges.Add(new RoofEdge { Segment = seg, Loop = b, IsBoundary = true });

            var ordered = openings
                .OrderByDescending(o => Math.Round(o.Loop.Centroid.Y / Tolerance))
                .ThenBy(o => Math.Round(o.Loop.Centroid.X / Tolerance))
                .ToList();

            var unmatchedDrains = (drains ?? Array.Empty<DrainItem>()).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                Loop2D loop = ordered[i].Loop;
                LoopShapeInfo shape = LoopClassifier.Classify(loop);
                DrainItem drain = TakeMatchingDrain(loop, unmatchedDrains);
                var model = new OpeningModel
                {
                    Index = i + 1,
                    Loop = loop,
                    Shape = shape,
                    SizeText = SizeText(shape),
                    Source = ordered[i].Source,
                    Drain = drain,
                    IsDrain = drain != null && shape.Shape == LoopShape.Circular
                };
                outline.Openings.Add(model);

                foreach (LoopSegment2D seg in loop.Segments)
                    outline.Edges.Add(new RoofEdge { Segment = seg, Loop = loop, IsBoundary = false, OpeningIndex = i + 1 });
            }

            if (unmatchedDrains.Count > 0)
                outline.Notes.Add($"{unmatchedDrains.Count} opening(s) found by the drain detection could not be matched to an opening here; they cannot be used as drains.");

            // 4 — existing shape points
            outline.ExistingVertices = ReadShapePoints(roof);
            if (!IsShapeEditingOn(roof))
                outline.Notes.Add("Shape editing is off on this roof — it is switched on when the steps run.");

            return outline;
        }

        /// <summary>Plan positions of the roof's shape points (empty when shape editing is off).</summary>
        public static List<P2> ReadShapePoints(RoofBase roof)
        {
            var result = new List<P2>();
            SlabShapeEditor editor = roof?.GetSlabShapeEditor();
            if (editor == null || !editor.IsEnabled) return result;
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                result.Add(CurveConverter.ToP2(v.Position));
            return result;
        }

        private static bool IsShapeEditingOn(RoofBase roof)
        {
            SlabShapeEditor editor = roof.GetSlabShapeEditor();
            return editor != null && editor.IsEnabled;
        }

        /// <summary>The drain whose loop curves all lie on <paramref name="loop"/>; removed from the list when found.</summary>
        private static DrainItem TakeMatchingDrain(Loop2D loop, List<DrainItem> drains)
        {
            const double matchTolerance = 10.0 / 304.8;
            DrainItem best = null;
            double bestScore = double.MaxValue;
            foreach (DrainItem d in drains)
            {
                var ends = (d.LoopCurves ?? new List<Curve>())
                    .SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                    .Select(CurveConverter.ToP2)
                    .ToList();
                if (ends.Count == 0) continue;
                double score = ends.Max(loop.DistanceTo);
                if (score <= matchTolerance && score < bestScore)
                {
                    best = d;
                    bestScore = score;
                }
            }

            if (best != null) drains.Remove(best);
            return best;
        }

        private static bool SameLoop(Loop2D a, Loop2D b) =>
            a.Centroid.DistanceTo(b.Centroid) <= Math.Max(Tolerance * 2, 0.01 * a.Perimeter) &&
            Math.Abs(a.Perimeter - b.Perimeter) <= 0.02 * Math.Max(a.Perimeter, b.Perimeter);

        private static double ToMm(double feet) => feet * 304.8;

        private static string SizeText(LoopShapeInfo s) => s.Shape switch
        {
            LoopShape.Circular => $"Ø {ToMm(s.SizeA):0}",
            _ => $"{ToMm(s.SizeA):0} × {ToMm(s.SizeB):0}"
        };
    }

    /// <summary>Revit curve → one plan segment (Z dropped).</summary>
    public static class CurveConverter
    {
        /// <summary>Plan position of a point.</summary>
        public static P2 ToP2(XYZ p) => new P2(p.X, p.Y);

        /// <summary>
        /// Line → line; arc → arc (bound arcs via start/mid/end so direction and sweeps over 180°
        /// are right; unbound = full circle); anything else → one polyline through its tessellation.
        /// Returns null for a curve too short to use.
        /// </summary>
        public static LoopSegment2D ToSegment(Curve curve)
        {
            if (curve == null) return null;

            if (curve is Line line)
                return new LineSegment2D(ToP2(line.GetEndPoint(0)), ToP2(line.GetEndPoint(1)));

            if (curve is Arc arc)
            {
                P2 c = ToP2(arc.Center);
                if (!arc.IsBound)
                {
                    double a0 = Math.Atan2(arc.XDirection.Y, arc.XDirection.X);
                    double sweep = arc.Normal.Z >= 0 ? 2 * Math.PI : -2 * Math.PI;
                    return new ArcSegment2D(c, arc.Radius, a0, sweep);
                }

                ArcSegment2D seg = ArcSegment2D.FromThreePoints(
                    c, arc.Radius,
                    ToP2(arc.GetEndPoint(0)),
                    ToP2(arc.Evaluate(0.5, true)),
                    ToP2(arc.GetEndPoint(1)));
                if (seg != null) return seg;
            }

            IList<XYZ> pts = curve.Tessellate();
            return pts.Count >= 2 ? new PolylineSegment2D(pts.Select(ToP2)) : null;
        }
    }
}
