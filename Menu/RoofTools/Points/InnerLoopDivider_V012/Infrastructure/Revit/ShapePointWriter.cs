// =======================================================
// File: ShapePointWriter.cs
// Location: Infrastructure/Revit/
// New in V012 — replaces V011's LoopDivisionService.
//   • ONE transaction: optional offset reset + all new points, so a single
//     Undo reverts the whole Apply.
//   • Offsets are reset only when the user ticks "Reset existing offsets
//     first" (V011 reset them on launch, every time).
//   • Points that already exist (within 5 mm) are skipped up front and
//     counted, instead of relying on AddPoint to throw.
//   • Each new point is placed ON the current top surface (vertical
//     projection onto the top faces), so adding a point never changes the
//     roof's shape — works on sloped / shape-edited roofs too.
// Must run inside a valid API context (ExternalEvent handler or command).
// =======================================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.InnerLoopDivider.V012.Core.Geometry;
using Revit26_Plugin_Revised.InnerLoopDivider.V012.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.InnerLoopDivider.V012.Infrastructure.Revit
{
    public sealed class ShapePointWriteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int Added { get; set; }
        public int SkippedExisting { get; set; }
        public int Failed { get; set; }
        public int OffsetsReset { get; set; }
        public int LoopsProcessed { get; set; }
        public List<P2> VerticesAfter { get; } = new List<P2>();
    }

    public static class ShapePointWriter
    {
        public const string TransactionName = "Inner Loop Divider – add points";

        public static ShapePointWriteResult Apply(Document doc, RoofBase roof, IEnumerable<RoofLoopModel> loops,
                                                  DivisionMode mode, double spacingFeet, bool resetOffsetsFirst)
        {
            var result = new ShapePointWriteResult();
            var selected = (loops ?? Enumerable.Empty<RoofLoopModel>()).Where(l => l != null && l.IsSelected && l.Loop != null).ToList();
            double tol = RoofLoopReader.Tolerance;

            using (var tx = new Transaction(doc, TransactionName))
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
                        editor.Enable();

                    if (resetOffsetsFirst)
                    {
                        foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                        {
                            editor.ModifySubElement(v, 0.0);
                            result.OffsetsReset++;
                        }
                    }

                    doc.Regenerate();

                    var existing = editor.SlabShapeVertices.Cast<SlabShapeVertex>()
                        .Select(v => CurveConverter.ToP2(v.Position)).ToList();
                    var surface = new TopSurface(roof);

                    foreach (RoofLoopModel loop in selected)
                    {
                        ToolCancellation.CheckCurrent();
                        DivisionPlan plan = DivisionPlanner.Plan(loop.Loop, mode, loop.RecommendedPoints, spacingFeet, existing, tol);
                        result.SkippedExisting += plan.ExistingPoints;
                        foreach (PlannedPoint p in plan.Points.Where(p => !p.AlreadyExists))
                        {
                            ToolCancellation.CheckCurrent();
                            try
                            {
                                editor.AddPoint(surface.PointOn(p.Position));
                                existing.Add(p.Position);
                                result.Added++;
                            }
                            catch (Autodesk.Revit.Exceptions.ArgumentException)
                            {
                                result.Failed++;
                            }
                            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                            {
                                result.Failed++;
                            }
                        }

                        result.LoopsProcessed++;
                    }

                    if (result.Added == 0 && result.OffsetsReset == 0)
                    {
                        tx.RollBack();
                        result.Success = true; // nothing to do is not an error
                    }
                    else if (tx.Commit() != TransactionStatus.Committed)
                    {
                        result.ErrorMessage = "Revit did not commit the transaction; nothing was changed.";
                        return result;
                    }
                    else
                    {
                        result.Success = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();
                    result.ErrorMessage = ex.Message;
                    return result;
                }
            }

            SlabShapeEditor after = roof.GetSlabShapeEditor();
            if (after != null && after.IsEnabled)
            {
                foreach (SlabShapeVertex v in after.SlabShapeVertices)
                    result.VerticesAfter.Add(CurveConverter.ToP2(v.Position));
            }

            return result;
        }

        /// <summary>Vertical projection of plan points onto the roof's top faces.</summary>
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

                // Non-planar faces: settle on the closest point, keeping plan X/Y.
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
