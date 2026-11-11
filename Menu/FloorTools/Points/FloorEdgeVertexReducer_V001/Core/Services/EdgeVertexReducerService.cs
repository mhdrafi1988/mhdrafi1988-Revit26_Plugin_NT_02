using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Core.Services
{
    /// <summary>
    /// Pure geometry/logic layer. No UI, no ExternalEvent plumbing — only called from
    /// inside a valid Revit API context (see FloorEdgeVertexReducerEventHandler).
    ///
    /// Assumptions confirmed with user:
    ///  - Segments come from Floor.GetProfile() (outer + inner sketch loops).
    ///  - Only Line curves become segments; Arc/Spline edges are skipped and any
    ///    vertex near them is left untouched.
    ///  - "On segment" = 5mm-class perpendicular distance, measured in the XY plane
    ///    only (Z is ignored for matching — shape-editor points move in Z, not XY).
    ///  - Start/end vertex = first/last match along the segment in loop-traversal order.
    ///  - Tie on max interior Z -> first in loop-traversal order wins.
    ///
    ///  V002 addition — outer vs. inner loops now have different interior rules:
    ///  - Inner loop segments: unchanged. Keep start, end, and the max-Z interior
    ///    point only if it clears both ends. Max 3 points kept per segment.
    ///  - Outer loop segments: same qualifying max-Z rule, but if it qualifies,
    ///    also keep the interior point exactly N positions away (loop-traversal
    ///    order) on each side, if one exists at that index. N = NeighborOffset,
    ///    user-set from the UI. Max 5 points kept per segment. If the max-Z point
    ///    does not qualify, no neighbors are kept either — same fallback as inner.
    /// </summary>
    public static class EdgeVertexReducerService
    {
        public static List<FloorSegment> BuildSegments(Element floor, out List<string> skipped)
        {
            skipped = new List<string>();
            var segments = new List<FloorSegment>();

            if (!(floor is Floor footprint))
            {
                skipped.Add("Element is not a Floor — cannot read sketch profile.");
                return segments;
            }

            // A Floor has no GetProfiles(); its Sketch is read instead (floor port, 6.34.0).
            List<List<Curve>> loops = FloorSketchProfile.GetLoops(footprint);

            if (loops.Count == 0)
            {
                skipped.Add("No boundary loops found on floor sketch.");
                return segments;
            }

            // Largest |area| loop = outer boundary; the rest are inner (openings).
            var loopAreas = loops.Select(l => Math.Abs(SignedAreaXY(l))).ToList();
            int outerIndex = loopAreas.IndexOf(loopAreas.Max());

            int innerCount = 0;
            for (int li = 0; li < loops.Count; li++)
            {
                string loopLabel = li == outerIndex ? "Outer" : $"Inner {++innerCount}";
                var curves = loops[li];

                for (int ci = 0; ci < curves.Count; ci++)
                {
                    if (curves[ci] is Line line)
                    {
                        var startXY = Flatten(line.GetEndPoint(0));
                        var endXY = Flatten(line.GetEndPoint(1));

                        segments.Add(new FloorSegment
                        {
                            LoopLabel = loopLabel,
                            SegmentIndex = ci,
                            Line = line,
                            StartXY = startXY,
                            EndXY = endXY,
                            LengthXY = startXY.DistanceTo(endXY)
                        });
                    }
                    else
                    {
                        skipped.Add($"{loopLabel} / seg {ci + 1}: non-straight curve ({curves[ci].GetType().Name}) — left untouched.");
                    }
                }
            }

            return segments;
        }

        /// <summary>
        /// Classifies every current SlabShapeEditor vertex against the straight segments
        /// and applies the keep/remove rule per segment. Read-only — never modifies the model.
        /// </summary>
        /// <param name="neighborOffset">
        /// Outer-loop only. Number of positions (loop-traversal order) away from a
        /// qualifying max-Z point to also keep, on each side. 0 disables the neighbor
        /// rule entirely (behaves like inner loops).
        /// </param>
        public static List<VertexDecision> ClassifyAndReduce(
            SlabShapeEditor editor,
            List<FloorSegment> segments,
            double toleranceFeet,
            int neighborOffset = 0)
        {
            var decisions = new List<VertexDecision>();
            if (editor == null) return decisions;

            var vertices = new List<SlabShapeVertex>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                vertices.Add(v);

            // Assign each vertex to its closest matching segment (within tolerance), by XY distance.
            var bySegment = new Dictionary<FloorSegment, List<(SlabShapeVertex v, double t)>>();
            var unmatched = new List<SlabShapeVertex>();

            foreach (var v in vertices)
            {
                ToolCancellation.CheckCurrent();
                XYZ p = Flatten(v.Position);
                FloorSegment best = null;
                double bestDist = double.MaxValue;
                double bestT = 0;

                foreach (var seg in segments)
                {
                    double t = ProjectParameter(seg.StartXY, seg.EndXY, p);
                    double tClamped = Math.Max(0, Math.Min(1, t));
                    XYZ closest = seg.StartXY + (seg.EndXY - seg.StartXY) * tClamped;
                    double dist = p.DistanceTo(closest);

                    if (dist <= toleranceFeet && dist < bestDist)
                    {
                        best = seg;
                        bestDist = dist;
                        bestT = t;
                    }
                }

                if (best != null)
                {
                    if (!bySegment.TryGetValue(best, out var list))
                    {
                        list = new List<(SlabShapeVertex, double)>();
                        bySegment[best] = list;
                    }
                    list.Add((v, bestT));
                }
                else
                {
                    unmatched.Add(v);
                }
            }

            foreach (var v in unmatched)
            {
                decisions.Add(new VertexDecision
                {
                    Vertex = v,
                    Position = v.Position,
                    ZFeet = v.Position.Z,
                    SegmentLabel = "—",
                    Action = VertexAction.KeepUnmatched,
                    Reason = "Not within tolerance of any straight segment"
                });
            }

            foreach (var kv in bySegment)
            {
                ToolCancellation.CheckCurrent();
                var seg = kv.Key;
                var ordered = kv.Value.OrderBy(x => x.t).ToList();
                if (ordered.Count == 0) continue;

                // First/last by loop-traversal (parameter) order — kept unconditionally.
                var startEntry = ordered.First();
                var endEntry = ordered.Last();

                decisions.Add(new VertexDecision
                {
                    Vertex = startEntry.v,
                    Position = startEntry.v.Position,
                    ZFeet = startEntry.v.Position.Z,
                    SegmentLabel = seg.Label,
                    Action = VertexAction.KeepStart,
                    Reason = "Segment start vertex"
                });

                if (ordered.Count == 1)
                    continue; // single point on this segment — nothing interior

                if (!ReferenceEquals(startEntry.v, endEntry.v))
                {
                    decisions.Add(new VertexDecision
                    {
                        Vertex = endEntry.v,
                        Position = endEntry.v.Position,
                        ZFeet = endEntry.v.Position.Z,
                        SegmentLabel = seg.Label,
                        Action = VertexAction.KeepEnd,
                        Reason = "Segment end vertex"
                    });
                }

                var interior = ordered.Skip(1).Take(ordered.Count - 2).ToList();
                if (interior.Count == 0) continue;

                double startZ = startEntry.v.Position.Z;
                double endZ = endEntry.v.Position.Z;

                double maxZ = interior.Max(x => x.v.Position.Z);
                int maxIndex = interior.FindIndex(x => Math.Abs(x.v.Position.Z - maxZ) < 1e-9);
                bool qualifies = maxZ > startZ + 1e-9 && maxZ > endZ + 1e-9;

                bool isOuter = string.Equals(seg.LoopLabel, "Outer", StringComparison.Ordinal);

                // Interior indices kept as "neighbors" of the max-Z point — outer loops only,
                // and only when the max-Z point itself qualifies. neighborOffset <= 0 disables this.
                var neighborIndices = new HashSet<int>();
                if (qualifies && isOuter && neighborOffset > 0)
                {
                    int leftIndex = maxIndex - neighborOffset;
                    int rightIndex = maxIndex + neighborOffset;
                    if (leftIndex >= 0) neighborIndices.Add(leftIndex);
                    if (rightIndex < interior.Count) neighborIndices.Add(rightIndex);
                }

                for (int i = 0; i < interior.Count; i++)
                {
                    var entry = interior[i];
                    bool isMax = i == maxIndex;

                    if (isMax && qualifies)
                    {
                        decisions.Add(new VertexDecision
                        {
                            Vertex = entry.v,
                            Position = entry.v.Position,
                            ZFeet = entry.v.Position.Z,
                            SegmentLabel = seg.Label,
                            Action = VertexAction.KeepMaxZ,
                            Reason = $"Highest interior Z ({FeetToMm(maxZ):0.0}mm), above both ends"
                        });
                    }
                    else if (neighborIndices.Contains(i))
                    {
                        decisions.Add(new VertexDecision
                        {
                            Vertex = entry.v,
                            Position = entry.v.Position,
                            ZFeet = entry.v.Position.Z,
                            SegmentLabel = seg.Label,
                            Action = VertexAction.KeepNeighbor,
                            Reason = $"Outer boundary — {neighborOffset} point(s) from max-Z point"
                        });
                    }
                    else if (isMax)
                    {
                        decisions.Add(new VertexDecision
                        {
                            Vertex = entry.v,
                            Position = entry.v.Position,
                            ZFeet = entry.v.Position.Z,
                            SegmentLabel = seg.Label,
                            Action = VertexAction.Remove,
                            Reason = "Highest interior Z but not above both ends"
                        });
                    }
                    else
                    {
                        decisions.Add(new VertexDecision
                        {
                            Vertex = entry.v,
                            Position = entry.v.Position,
                            ZFeet = entry.v.Position.Z,
                            SegmentLabel = seg.Label,
                            Action = VertexAction.Remove,
                            Reason = "Interior vertex, not the retained max-Z point"
                        });
                    }
                }
            }

            return decisions;
        }

        /// <summary>Removes every vertex marked Remove. Per-item try/catch — failures are logged, not thrown.</summary>
        public static int ApplyRemovals(SlabShapeEditor editor, List<VertexDecision> decisions, Action<string> logSkip)
        {
            int removed = 0;
            foreach (var d in decisions.Where(d => d.WillRemove))
            {
                ToolCancellation.CheckCurrent();
                try
                {
                    // Use DeletePoint instead of RemovePoint
                    if (editor.DeletePoint(d.Vertex))
                    {
                        removed++;
                    }
                    else
                    {
                        logSkip?.Invoke($"{d.SegmentLabel}: could not remove point at Z={FeetToMm(d.ZFeet):0.0}mm — DeletePoint returned false");
                    }
                }
                catch (Exception ex)
                {
                    logSkip?.Invoke($"{d.SegmentLabel}: could not remove point at Z={FeetToMm(d.ZFeet):0.0}mm — {ex.Message}");
                }
            }
            return removed;
        }

        private static XYZ Flatten(XYZ p) => new XYZ(p.X, p.Y, 0);

        private static double ProjectParameter(XYZ start, XYZ end, XYZ p)
        {
            XYZ dir = end - start;
            double len2 = dir.DotProduct(dir);
            if (len2 < 1e-12) return 0;
            return (p - start).DotProduct(dir) / len2;
        }

        private static double SignedAreaXY(List<Curve> curves)
        {
            var pts = new List<XYZ>();
            foreach (var c in curves)
                pts.AddRange(c.Tessellate());

            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                area += (a.X * b.Y) - (b.X * a.Y);
            }
            return area / 2.0;
        }

        public static double FeetToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        public static double MmToFeet(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    }
}
