using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>
    /// Reads a room's boundary and turns it into a closed outline in host coordinates (mm).
    /// Curved boundaries are faceted, because the repair chain works on straight pieces.
    /// </summary>
    public static class BoundaryReader
    {
        /// <summary>Feet to millimetres.</summary>
        public const double FtToMm = 304.8;

        /// <summary>Square feet to square metres.</summary>
        public const double Ft2ToM2 = 0.09290304;

        private static readonly SpatialElementBoundaryLocation[] Locations =
        {
            SpatialElementBoundaryLocation.Finish,
            SpatialElementBoundaryLocation.Center,
            SpatialElementBoundaryLocation.CoreBoundary,
            SpatialElementBoundaryLocation.CoreCenter
        };

        /// <summary>
        /// Outline of <paramref name="room"/>. The finish-face boundary is used when it closes
        /// cleanly; otherwise the other boundary locations are tried (method 1) and the best result
        /// is kept. When none closes, the room's 3D shape and, if allowed, a hull are tried.
        /// </summary>
        /// <param name="room">Room in the host or in a linked document.</param>
        /// <param name="toHost">Link transform; identity for host rooms.</param>
        /// <param name="s">Tolerances.</param>
        public static RoomOutline Read(Room room, Transform toHost, WetAreaSettings s)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            toHost ??= Transform.Identity;
            double roomAreaM2 = room.Area * Ft2ToM2;

            // Approximate outlines are only for the last attempt, on the finish boundary.
            var strict = new WetAreaSettings
            {
                SnapMm = s.SnapMm, BridgeMm = s.BridgeMm, AreaTolerancePct = s.AreaTolerancePct, AllowApproximate = false
            };

            IReadOnlyList<IReadOnlyList<Seg>> finishLoops = Loops(room, Locations[0], toHost);
            RoomOutline finish = LoopRepair.Evaluate(finishLoops, roomAreaM2, strict, null);
            if (finish.Status == RoomStatus.Ready || finish.Status == RoomStatus.NotEnclosed) return finish;

            RoomOutline best = finish;
            SpatialElementBoundaryLocation bestLocation = Locations[0];
            foreach (SpatialElementBoundaryLocation location in Locations.Skip(1))
            {
                RoomOutline other = LoopRepair.Evaluate(Loops(room, location, toHost), roomAreaM2, strict, null);
                if (Rank(other.Status) < Rank(best.Status))
                {
                    best = other;
                    bestLocation = location;
                }
                if (best.Status == RoomStatus.Ready) break;
            }

            if (bestLocation != Locations[0])
            {
                var steps = new List<RepairStep>
                {
                    new(1, "Boundary location", true, $"outline closed at the {Describe(bestLocation)} instead of the finish face", null)
                };
                steps.AddRange(best.Steps);
                return best with { Steps = steps, Status = best.Status == RoomStatus.Ready ? RoomStatus.Repaired : best.Status };
            }
            if (finish.Status != RoomStatus.Failed) return finish;

            // Nothing closed: last attempts on the finish boundary with the 3D shape and the hull.
            RoomOutline last = LoopRepair.Evaluate(finishLoops, roomAreaM2, s, () => BottomFace(room, toHost));
            var all = new List<RepairStep>
            {
                new(1, "Boundary location", false, "no boundary location gave a closed outline", null)
            };
            all.AddRange(last.Steps);
            return last with { Steps = all };
        }

        private static int Rank(RoomStatus status) => status switch
        {
            RoomStatus.Ready => 0,
            RoomStatus.Repaired => 1,
            RoomStatus.AreaWarning => 2,
            RoomStatus.Approximate => 3,
            _ => 4
        };

        private static string Describe(SpatialElementBoundaryLocation location) => location switch
        {
            SpatialElementBoundaryLocation.Center => "wall centre",
            SpatialElementBoundaryLocation.CoreBoundary => "core face",
            SpatialElementBoundaryLocation.CoreCenter => "core centre",
            _ => "finish face"
        };

        private static IReadOnlyList<IReadOnlyList<Seg>> Loops(Room room, SpatialElementBoundaryLocation location, Transform toHost)
        {
            var result = new List<IReadOnlyList<Seg>>();
            IList<IList<BoundarySegment>> loops;
            try
            {
                loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = location });
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return result; // Revit could not compute the boundary at this location
            }
            if (loops == null) return result;

            foreach (IList<BoundarySegment> loop in loops)
            {
                var segs = new List<Seg>();
                foreach (BoundarySegment piece in loop)
                {
                    Curve curve = piece?.GetCurve();
                    if (curve == null) continue;
                    AddCurve(segs, curve, toHost);
                }
                if (segs.Count > 0) result.Add(segs);
            }
            return result;
        }

        /// <summary>Adds a curve as straight pieces; lines stay one piece, anything else is faceted.</summary>
        private static void AddCurve(List<Seg> segs, Curve curve, Transform toHost)
        {
            IList<XYZ> points = curve is Line
                ? new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) }
                : curve.Tessellate();
            for (int i = 0; i + 1 < points.Count; i++)
                segs.Add(new Seg(ToPt(toHost.OfPoint(points[i])), ToPt(toHost.OfPoint(points[i + 1]))));
        }

        /// <summary>Host-coordinate plan point in mm.</summary>
        public static Pt ToPt(XYZ p) => new(p.X * FtToMm, p.Y * FtToMm);

        /// <summary>Outer outline of the bottom face of the room's solid (method 8).</summary>
        private static IReadOnlyList<Pt> BottomFace(Room room, Transform toHost)
        {
            using var calculator = new SpatialElementGeometryCalculator(room.Document);
            Solid solid = calculator.CalculateSpatialElementGeometry(room)?.GetGeometry();
            if (solid == null) return new List<Pt>();

            PlanarFace bottom = null;
            foreach (Face face in solid.Faces)
                if (face is PlanarFace planar && planar.FaceNormal.Z < -0.99 && (bottom == null || planar.Area > bottom.Area))
                    bottom = planar;
            if (bottom == null) return new List<Pt>();

            List<Pt> outer = null;
            double bestArea = 0;
            foreach (CurveLoop loop in bottom.GetEdgesAsCurveLoops())
            {
                var segs = new List<Seg>();
                foreach (Curve curve in loop) AddCurve(segs, curve, toHost);
                var pts = segs.Select(g => g.A).ToList();
                double area = Polyline2D.AreaM2(pts);
                if (area > bestArea) { bestArea = area; outer = pts; }
            }
            return outer ?? new List<Pt>();
        }
    }
}
