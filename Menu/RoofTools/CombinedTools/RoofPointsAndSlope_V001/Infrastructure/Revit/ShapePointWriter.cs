// =======================================================
// File: ShapePointWriter.cs
// Location: Infrastructure/Revit/
// Adds one step's planned points in ONE transaction (the engine runs it
// inside the run's TransactionGroup). Combines:
//   • Inner Loop Divider V012's writer — points that already exist
//     (within 5 mm) are skipped and counted; each new point is placed ON
//     the current top surface, so adding it never changes the roof shape.
//   • Inner Loops + Perpendicular V007's retry — when Revit rejects a
//     point on an edge, it is nudged into the roof 3, 8, 15, 30 and 60 mm.
// Must run inside a valid Revit API context.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Planning;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.Revit
{
    /// <summary>Outcome of writing one step's points.</summary>
    public sealed class ShapePointWriteResult
    {
        /// <summary>False when the transaction could not be committed; nothing was changed.</summary>
        public bool Success { get; set; }

        /// <summary>Why it failed.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Points added.</summary>
        public int Added { get; set; }

        /// <summary>Points added only after a nudge into the roof.</summary>
        public int Nudged { get; set; }

        /// <summary>Points skipped because a shape point is already there.</summary>
        public int AlreadyThere { get; set; }

        /// <summary>Points Revit rejected at every nudge.</summary>
        public int Rejected { get; set; }
    }

    /// <summary>Writes planned points into a roof's shape editor.</summary>
    public static class ShapePointWriter
    {
        private static readonly double[] NudgeStepsMm = { 3, 8, 15, 30, 60 };

        /// <summary>Adds <paramref name="points"/> to <paramref name="roof"/> in one transaction named <paramref name="transactionName"/>.</summary>
        public static ShapePointWriteResult Write(Document doc, RoofBase roof, IEnumerable<NewPoint> points, string transactionName)
        {
            var result = new ShapePointWriteResult();
            var toAdd = (points ?? Enumerable.Empty<NewPoint>()).ToList();
            if (toAdd.Count == 0)
            {
                result.Success = true;
                return result;
            }

            using (var tx = new Transaction(doc, transactionName))
            {
                tx.Start();
                try
                {
                    SlabShapeEditor editor = roof.GetSlabShapeEditor();
                    if (editor == null)
                    {
                        tx.RollBack();
                        result.ErrorMessage = "This roof does not support shape editing.";
                        return result;
                    }

                    if (!editor.IsEnabled)
                    {
                        editor.Enable();
                        doc.Regenerate();
                    }

                    var known = editor.SlabShapeVertices.Cast<SlabShapeVertex>()
                        .Select(v => CurveConverter.ToP2(v.Position)).ToList();
                    var surface = new TopSurface(roof);

                    foreach (NewPoint p in toAdd)
                    {
                        if (known.Any(k => k.DistanceTo(p.Position) <= PointPipelinePlanner.Tolerance))
                        {
                            result.AlreadyThere++;
                            continue;
                        }

                        if (TryAdd(editor, surface, p.Position))
                        {
                            known.Add(p.Position);
                            result.Added++;
                            continue;
                        }

                        bool placed = false;
                        if (p.Inward.Length > 0.5)
                        {
                            foreach (double mm in NudgeStepsMm)
                            {
                                P2 moved = p.Position + p.Inward * (mm / 304.8);
                                if (TryAdd(editor, surface, moved))
                                {
                                    known.Add(moved);
                                    result.Added++;
                                    result.Nudged++;
                                    placed = true;
                                    break;
                                }
                            }
                        }

                        if (!placed) result.Rejected++;
                    }

                    if (result.Added == 0)
                    {
                        tx.RollBack();
                        result.Success = true; // nothing new to add is not an error
                    }
                    else if (tx.Commit() == TransactionStatus.Committed)
                    {
                        result.Success = true;
                    }
                    else
                    {
                        result.ErrorMessage = "Revit did not commit the transaction.";
                    }
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();
                    result.ErrorMessage = ex.Message;
                }
            }

            return result;
        }

        private static bool TryAdd(SlabShapeEditor editor, TopSurface surface, P2 p)
        {
            try
            {
                editor.AddPoint(surface.PointOn(p));
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return false;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Vertical projection of plan points onto the roof's top faces (ported from Inner Loop Divider V012).</summary>
        private sealed class TopSurface
        {
            private readonly List<Face> _faces;
            private readonly double _fallbackZ;

            public TopSurface(RoofBase roof)
            {
                _faces = HostObjectUtils.GetTopFaces(roof)
                    .Select(r => roof.GetGeometryObjectFromReference(r) as Face)
                    .Where(f => f != null)
                    .ToList();
                BoundingBoxXYZ bb = roof.get_BoundingBox(null);
                _fallbackZ = bb?.Max.Z ?? 0.0;
            }

            public XYZ PointOn(P2 p)
            {
                const double onFace = 1e-3; // ft
                foreach (Face f in _faces)
                {
                    if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) > 1e-6)
                    {
                        XYZ n = pf.FaceNormal, o = pf.Origin;
                        double z = o.Z - ((p.X - o.X) * n.X + (p.Y - o.Y) * n.Y) / n.Z;
                        var candidate = new XYZ(p.X, p.Y, z);
                        IntersectionResult hit = pf.Project(candidate);
                        if (hit != null && hit.Distance <= onFace)
                            return candidate;
                    }
                }

                // Non-planar faces (or a point just off every face): closest point, keeping plan X/Y.
                XYZ q = new XYZ(p.X, p.Y, _fallbackZ);
                double bestDist = double.MaxValue;
                XYZ best = q;
                foreach (Face f in _faces)
                {
                    IntersectionResult hit = f.Project(q);
                    if (hit == null) continue;
                    IntersectionResult again = f.Project(new XYZ(p.X, p.Y, hit.XYZPoint.Z));
                    IntersectionResult use = again ?? hit;
                    if (use.Distance < bestDist)
                    {
                        bestDist = use.Distance;
                        best = new XYZ(p.X, p.Y, use.XYZPoint.Z);
                    }
                }

                return best;
            }
        }
    }
}
