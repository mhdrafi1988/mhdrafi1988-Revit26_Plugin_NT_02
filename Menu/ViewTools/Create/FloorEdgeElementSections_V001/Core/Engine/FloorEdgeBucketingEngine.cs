using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Extracts the boundary edges of a floor and buckets them into 4 view-aligned
    /// directions (North/South/East/West of the ACTIVE VIEW's rotation — not
    /// Project/True North). Copied verbatim from FloorEdgeAroundSections_V004 —
    /// this engine is floor-only geometry and has no dependency on picked elements.
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
        /// "North" means "top of the current view".
        /// </summary>
        public static Dictionary<EdgeDirection, BucketedEdge> BucketEdges(
            Floor floor,
            BoundingBoxXYZ floorBoundingBox,
            double viewRotationRadians,
            IList<LogEntry> log)
        {
            var result = new Dictionary<EdgeDirection, BucketedEdge>();

            // 1. Collect all boundary loop curves — outer AND inner loops.
            var allCurves = CollectBoundaryCurves(floor, log);
            if (allCurves.Count == 0)
            {
                log.Add(new LogEntry(LogLevel.Warning, $"Floor {floor.Id.Value}: no boundary curves found on the floor's solid geometry."));
                return result;
            }

            // 2. Build a view-aligned frame: rotate the world so view "up" = +Y.
            XYZ center = (floorBoundingBox.Min + floorBoundingBox.Max) * 0.5;
            Transform toViewAligned = Transform.CreateRotationAtPoint(XYZ.BasisZ, -viewRotationRadians, center);

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
                [EdgeDirection.North] = (new XYZ(midX, alignedMaxY, 0), XYZ.BasisX),
                [EdgeDirection.South] = (new XYZ(midX, alignedMinY, 0), XYZ.BasisX),
                [EdgeDirection.East]  = (new XYZ(alignedMaxX, midY, 0), XYZ.BasisY),
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

        private static XYZ GetCurveMidpoint(Curve curve)
        {
            double t0 = curve.GetEndParameter(0);
            double t1 = curve.GetEndParameter(1);
            double tMid = (t0 + t1) * 0.5;
            return curve.Evaluate(tMid, false);
        }

        private static double AngleBetweenLinesDegrees(XYZ a, XYZ b)
        {
            double dot = Math.Abs(a.X * b.X + a.Y * b.Y);
            dot = Math.Min(1.0, Math.Max(-1.0, dot));
            double angleRad = Math.Acos(dot);
            return angleRad * 180.0 / Math.PI;
        }

        private static XYZ ComputeInwardNormal(Curve curve, XYZ floorCenter)
        {
            XYZ mid = GetCurveMidpoint(curve);
            XYZ tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
            XYZ perp1 = new XYZ(-tangent.Y, tangent.X, 0).Normalize();
            XYZ perp2 = perp1.Negate();

            XYZ towardCenter = (floorCenter - mid).Normalize();
            return (perp1.DotProduct(towardCenter) >= perp2.DotProduct(towardCenter)) ? perp1 : perp2;
        }
    }
}
