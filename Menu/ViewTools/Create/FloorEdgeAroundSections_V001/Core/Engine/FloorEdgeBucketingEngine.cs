using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// Extracts the boundary edges of a floor and buckets them into 4 view-aligned
    /// directions (North/South/East/West of the ACTIVE VIEW's rotation — not
    /// Project/True North).
    ///
    /// Algorithm (per confirmed spec):
    ///   1. Compute the floor's bounding box, transformed into view-aligned space
    ///      (rotate by -viewRotation so the view's "up" becomes +Y).
    ///   2. Derive 4 conceptual bounding-box side midpoints (N/S/E/W).
    ///   3. For each side, scan all boundary loop curves (outer AND inner loops —
    ///      "any edge" per spec point 3/4) and pick the curve whose midpoint is
    ///      closest to that side's midpoint AND whose direction is roughly
    ///      parallel to that side (within a tolerance angle).
    ///   4. If no curve is close/parallel enough for a given side, that direction
    ///      is left unassigned (caller logs NoEdgeFound and skips it).
    ///
    /// The section cut direction itself (computed elsewhere) stays perpendicular
    /// to the ACTUAL selected edge, not forced to the compass axis — only the
    /// "which edge" and "what to call it" decisions are compass-bucketed.
    /// </summary>
    public static class FloorEdgeBucketingEngine
    {
        /// <summary>
        /// Max angle (degrees) between a candidate edge and the bounding-box side
        /// direction for it to be considered "roughly parallel" to that side.
        /// </summary>
        private const double ParallelToleranceDegrees = 30.0;

        public class BucketedEdge
        {
            public EdgeDirection Direction { get; set; }
            public Curve Curve { get; set; }
            public XYZ Midpoint { get; set; }
            public XYZ InwardNormal { get; set; }
            public double LengthFeet { get; set; }
        }

        /// <summary>
        /// Buckets a floor's boundary edges into up to 4 directions.
        /// viewRotationRadians: the active view's rotation angle (radians), used so
        /// "North" means "top of the current view", per confirmed spec point 9b.
        /// </summary>
        public static Dictionary<EdgeDirection, BucketedEdge> BucketEdges(
            Floor floor,
            BoundingBoxXYZ floorBoundingBox,
            double viewRotationRadians,
            IList<LogEntry> log)
        {
            var result = new Dictionary<EdgeDirection, BucketedEdge>();

            // 1. Collect all boundary loop curves — outer AND inner loops (any edge, per spec).
            var allCurves = CollectBoundaryCurves(floor, log);
            if (allCurves.Count == 0)
            {
                log.Add(new LogEntry(LogLevel.Warning, $"Floor {floor.Id.Value}: no boundary curves found on the floor's solid geometry."));
                return result;
            }

            // 2. Build a view-aligned frame: rotate the world so view "up" = +Y.
            //    We rotate points by -viewRotationRadians around Z at the bbox center.
            XYZ center = (floorBoundingBox.Min + floorBoundingBox.Max) * 0.5;
            Transform toViewAligned = Transform.CreateRotationAtPoint(XYZ.BasisZ, -viewRotationRadians, center);
            Transform toWorld = toViewAligned.Inverse;

            // Transform bbox corners into view-aligned space to get an aligned min/max.
            XYZ min = floorBoundingBox.Min;
            XYZ max = floorBoundingBox.Max;
            var corners = new[]
            {
                new XYZ(min.X, min.Y, 0), new XYZ(max.X, min.Y, 0),
                new XYZ(max.X, max.Y, 0), new XYZ(min.X, max.Y, 0)
            }.Select(p => toViewAligned.OfPoint(p)).ToList();

            double alignedMinX = corners.Min(p => p.X);
            double alignedMaxX = corners.Max(p => p.X);
            double alignedMinY = corners.Min(p => p.Y);
            double alignedMaxY = corners.Max(p => p.Y);
            double midX = (alignedMinX + alignedMaxX) * 0.5;
            double midY = (alignedMinY + alignedMaxY) * 0.5;

            // 3. Define the 4 side midpoints + side direction vectors, in view-aligned space.
            var sideDefs = new Dictionary<EdgeDirection, (XYZ midpoint, XYZ sideDir)>
            {
                [EdgeDirection.North] = (new XYZ(midX, alignedMaxY, 0), XYZ.BasisX), // top edge runs horizontally
                [EdgeDirection.South] = (new XYZ(midX, alignedMinY, 0), XYZ.BasisX),
                [EdgeDirection.East]  = (new XYZ(alignedMaxX, midY, 0), XYZ.BasisY), // right edge runs vertically
                [EdgeDirection.West]  = (new XYZ(alignedMinX, midY, 0), XYZ.BasisY),
            };

            // 4. Precompute curve midpoints + directions in view-aligned space once.
            var candidateInfo = allCurves.Select(c =>
            {
                XYZ worldMid = GetCurveMidpoint(c);
                XYZ alignedMid = toViewAligned.OfPoint(worldMid);
                XYZ worldDir = (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize();
                XYZ alignedDir = toViewAligned.OfVector(worldDir).Normalize();
                return new { Curve = c, AlignedMid = alignedMid, AlignedDir = alignedDir, WorldMid = worldMid };
            }).ToList();

            // 5. For each side, pick the closest+parallel-enough curve.
            foreach (var kvp in sideDefs)
            {
                EdgeDirection dir = kvp.Key;
                XYZ sideMid = kvp.Value.midpoint;
                XYZ sideDir = kvp.Value.sideDir;

                var best = candidateInfo
                    .Select(c => new
                    {
                        c.Curve,
                        c.AlignedMid,
                        c.WorldMid,
                        Distance = c.AlignedMid.DistanceTo(sideMid),
                        AngleDeg = AngleBetweenLinesDegrees(c.AlignedDir, sideDir)
                    })
                    .Where(c => c.AngleDeg <= ParallelToleranceDegrees)
                    .OrderBy(c => c.Distance)
                    .FirstOrDefault();

                if (best == null)
                {
                    log.Add(new LogEntry(LogLevel.Warning,
                        $"Floor {floor.Id.Value}: no edge reasonably parallel/close to {dir} side — skipped."));
                    continue;
                }

                XYZ inward = ComputeInwardNormal(best.Curve, center);

                result[dir] = new BucketedEdge
                {
                    Direction = dir,
                    Curve = best.Curve,
                    Midpoint = best.WorldMid,
                    InwardNormal = inward,
                    LengthFeet = best.Curve.Length
                };

                log.Add(new LogEntry(LogLevel.Info,
                    $"Floor {floor.Id.Value}: {dir} edge selected, length {UnitUtils.ConvertFromInternalUnits(best.Curve.Length, UnitTypeId.Millimeters):F0} mm."));
            }

            return result;
        }

        /// <summary>
        /// Collects every curve from the floor's ACTUAL built solid geometry (bottom-most
        /// near-horizontal face's edge loops) — not the flat sketch.
        ///
        /// Floor.GetProfiles() was tried first but rejected: it returns the
        /// original sketch curves, which always sit flat at the floor's Level + Base
        /// Offset elevation, regardless of slope arrows or shape-edited sub-elements.
        /// On any sloped floor this put every section at the sketch's flat elevation
        /// instead of the real, per-edge elevation of the finished floor surface —
        /// confirmed as the root cause of sections landing at the wrong height.
        /// Reading the real solid via get_Geometry() tracks the built floor, including
        /// slope and shape edits, for every Floor-derived type.
        /// </summary>
        private static List<Curve> CollectBoundaryCurves(Floor floor, IList<LogEntry> log)
        {
            var curves = new List<Curve>();
            try
            {
                Options opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                GeometryElement geomElem = floor.get_Geometry(opts);
                if (geomElem != null)
                {
                    foreach (GeometryObject obj in geomElem)
                    {
                        if (obj is Solid solid && solid.Faces.Size > 0)
                        {
                            // Use the bottom-most near-horizontal face's edge loop as the
                            // boundary — this follows the real, current floor surface
                            // (slope/shape edits included), not the flat original sketch.
                            Face bottomFace = FindBottomFace(solid);
                            if (bottomFace != null)
                            {
                                foreach (EdgeArray loop in bottomFace.EdgeLoops)
                                {
                                    foreach (Edge e in loop)
                                        curves.Add(e.AsCurve());
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Add(new LogEntry(LogLevel.Warning, $"Floor {floor.Id.Value}: boundary curve extraction failed — {ex.Message}"));
            }

            return curves;
        }

        private static Face FindBottomFace(Solid solid)
        {
            Face bottom = null;
            double lowestZ = double.MaxValue;
            foreach (Face f in solid.Faces)
            {
                if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) > 0.9)
                {
                    double z = pf.Origin.Z;
                    if (z < lowestZ)
                    {
                        lowestZ = z;
                        bottom = f;
                    }
                }
            }
            return bottom;
        }

        /// <summary>
        /// Arc-aware curve midpoint: uses the curve's own parameterization midpoint
        /// (ComputeDerivatives at the mid-parameter), not the straight-line chord
        /// midpoint between endpoints — per confirmed spec point 5 ("best choice").
        /// For a Line this is identical to the chord midpoint; for an Arc it lies
        /// on the arc itself.
        /// </summary>
        private static XYZ GetCurveMidpoint(Curve curve)
        {
            double t0 = curve.GetEndParameter(0);
            double t1 = curve.GetEndParameter(1);
            double tMid = (t0 + t1) * 0.5;
            return curve.Evaluate(tMid, false);
        }

        private static double AngleBetweenLinesDegrees(XYZ a, XYZ b)
        {
            // Treat direction as a line (not a ray): fold to [0,90] so opposite-facing
            // parallel edges (e.g. two horizontal edges pointing left vs right) still match.
            double dot = Math.Abs(a.X * b.X + a.Y * b.Y); // ignore Z, 2D comparison in view-aligned space
            dot = Math.Min(1.0, Math.Max(-1.0, dot));
            double angleRad = Math.Acos(dot);
            return angleRad * 180.0 / Math.PI;
        }

        /// <summary>
        /// Computes the inward-facing normal at a boundary curve's midpoint —
        /// perpendicular to the curve's own tangent, pointing toward the floor's
        /// bounding-box center. Used as the section view direction (looking inward),
        /// per confirmed spec point 6/9c: cut stays perpendicular to the actual edge.
        /// </summary>
        private static XYZ ComputeInwardNormal(Curve curve, XYZ floorCenter)
        {
            XYZ mid = GetCurveMidpoint(curve);
            XYZ tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
            // Two perpendicular candidates in the horizontal plane:
            XYZ perp1 = new XYZ(-tangent.Y, tangent.X, 0).Normalize();
            XYZ perp2 = perp1.Negate();

            XYZ towardCenter = (floorCenter - mid).Normalize();
            // Pick whichever perpendicular candidate points more toward the floor center.
            return (perp1.DotProduct(towardCenter) >= perp2.DotProduct(towardCenter)) ? perp1 : perp2;
        }
    }
}
