// =======================================================
// File: FloorLoopReader.cs
// Location: Infrastructure/Revit/
// New in V012 — replaces the loop extraction in V011's
// FloorGeometryService, which only looked at top faces whose normal was
// exactly +Z (that is why V011 flattened the floor on launch), picked
// the outer loop by longest perimeter and restarted numbering per face.
//
// Openings now come from two read-only sources, so sloped or
// shape-edited floors work without touching them:
//   1. Floors: the sketch profiles (largest-area loop = outer,
//      every other loop = an opening).
//   2. Every floor: holes in the top faces (HostObjectUtils.GetTopFaces) —
//      catches shaft/opening cuts that are not in the sketch. A hole is
//      a face loop that winds the opposite way to the face's outer loop.
// Duplicates between the two are merged; loops are numbered 1..n in a
// stable order (north to south, then west to east).
// Also returns the floor's existing shape-editing points (plan XY) so
// planned points that already exist are not counted or added twice.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Infrastructure.Revit
{
    public sealed class FloorLoopReadResult
    {
        public List<FloorLoopModel> InnerLoops { get; } = new List<FloorLoopModel>();
        public List<P2> ExistingVertices { get; } = new List<P2>();
        public bool ShapeEditingEnabled { get; set; }
        public List<string> Notes { get; } = new List<string>();
    }

    public static class FloorLoopReader
    {
        /// <summary>5 mm in feet — loops/points closer than this are the same.</summary>
        public const double Tolerance = 5.0 / 304.8;

        public static FloorLoopReadResult Read(Floor floor)
        {
            if (floor == null) throw new ArgumentNullException(nameof(floor));
            var result = new FloorLoopReadResult();
            var openings = new List<(Loop2D Loop, string Source, CurveLoop Geometry)>();

            // 1 — sketch profiles. A Floor has no GetProfiles(); its Sketch is read
            // through Shared.Services.FloorSketchProfile instead (floor port, 6.34.0).
            {
                var sketchLoops = new List<Loop2D>();
                int n = 0;
                foreach (List<Curve> profile in FloorSketchProfile.GetLoops(floor))
                {
                    n++;
                    var segs = new List<LoopSegment2D>();
                    foreach (Curve curve in profile)
                        segs.AddRange(CurveConverter.ToSegments(curve));

                    Loop2D loop = LoopChainer.Chain(segs, Tolerance);
                    if (loop == null)
                        result.Notes.Add($"Sketch loop {n} is not closed — skipped.");
                    else
                        sketchLoops.Add(loop);
                }

                // An opening is a loop that sits inside a bigger loop. Separate
                // islands of the same floor are not openings.
                foreach (Loop2D l in sketchLoops)
                {
                    bool inside = sketchLoops.Any(o => !ReferenceEquals(o, l) && o.Area > l.Area && o.Contains(l.Samples[0]));
                    if (inside)
                        openings.Add((l, "Sketch", null));
                }
            }

            // 2 — holes in the top faces
            int faceCount = 0;
            foreach (Reference r in HostObjectUtils.GetTopFaces(floor))
            {
                if (floor.GetGeometryObjectFromReference(r) is not Face face) continue;
                faceCount++;

                var faceLoops = new List<(Loop2D Loop, CurveLoop Curves)>();
                foreach (CurveLoop cl in face.GetEdgesAsCurveLoops())
                {
                    var segs = cl.SelectMany(CurveConverter.ToSegments).ToList();
                    Loop2D loop = LoopChainer.Chain(segs, Tolerance);
                    if (loop != null && loop.Area > Tolerance * Tolerance)
                        faceLoops.Add((loop, cl));
                }

                if (faceLoops.Count < 2) continue;
                var outerFace = faceLoops.OrderByDescending(l => l.Loop.Area).First();
                int outerSign = Math.Sign(outerFace.Loop.SignedArea);
                foreach (var fl in faceLoops)
                {
                    if (ReferenceEquals(fl.Loop, outerFace.Loop) || Math.Sign(fl.Loop.SignedArea) == outerSign)
                        continue;

                    int match = openings.FindIndex(o => SameLoop(o.Loop, fl.Loop));
                    if (match >= 0)
                    {
                        if (openings[match].Geometry == null)
                            openings[match] = (openings[match].Loop, openings[match].Source, fl.Curves);
                        continue;
                    }

                    openings.Add((fl.Loop, "Opening cut", fl.Curves));
                }
            }

            if (faceCount == 0)
                result.Notes.Add("No top face found on the floor — only sketch openings were read.");

            // 3 — existing shape points
            SlabShapeEditor editor = floor.GetSlabShapeEditor();
            result.ShapeEditingEnabled = editor != null && editor.IsEnabled;
            if (result.ShapeEditingEnabled)
            {
                foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                    result.ExistingVertices.Add(CurveConverter.ToP2(v.Position));
            }
            else
            {
                result.Notes.Add("Shape editing is off on this floor — it is switched on at Apply. " +
                                 "Opening corners count as new until then.");
            }

            // 4 — stable numbering and models
            var ordered = openings
                .OrderByDescending(o => Math.Round(o.Loop.Centroid.Y / Tolerance))
                .ThenBy(o => Math.Round(o.Loop.Centroid.X / Tolerance))
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                LoopShapeInfo shape = LoopClassifier.Classify(ordered[i].Loop);
                result.InnerLoops.Add(new FloorLoopModel
                {
                    Index = i + 1,
                    LoopType = "Inner",
                    Source = ordered[i].Source,
                    Loop = ordered[i].Loop,
                    Shape = shape,
                    LoopShapeType = shape.Name,
                    IsCircular = shape.Shape == LoopShape.Circular,
                    PerimeterMm = ToMm(ordered[i].Loop.Perimeter),
                    SizeText = SizeText(shape),
                    Geometry = ordered[i].Geometry,
                    ExistingVertices = result.ExistingVertices
                });
            }

            return result;
        }

        private static bool SameLoop(Loop2D a, Loop2D b) =>
            a.Centroid.DistanceTo(b.Centroid) <= Math.Max(Tolerance * 2, 0.01 * a.Perimeter) &&
            Math.Abs(a.Perimeter - b.Perimeter) <= 0.02 * Math.Max(a.Perimeter, b.Perimeter);

        private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);

        private static string SizeText(LoopShapeInfo s) => s.Shape switch
        {
            LoopShape.Circular => $"Ø {ToMm(s.SizeA):0}",
            _ => $"{ToMm(s.SizeA):0} × {ToMm(s.SizeB):0}"
        };
    }
}
