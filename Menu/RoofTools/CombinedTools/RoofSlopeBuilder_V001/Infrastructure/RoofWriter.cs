// Applies the solved points, split lines and heights to one roof through the SlabShapeEditor.
// Must be called inside an open transaction.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Core;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Infrastructure
{
    /// <summary>Outcome of one roof, shown in the end-of-run report.</summary>
    public class RoofReport
    {
        public string Name;
        public bool Ok;
        public string Error;
        public int Drains, PointsAdded, PointsFailed, RidgePoints, SplitLines, SplitLinesFailed;
        public double MaxHeightMm;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>Writes the solved shape points, split lines and heights to one roof.</summary>
    public static class RoofWriter
    {
        /// <summary>
        /// Applies the slope to <paramref name="roof"/>. Must run inside an open transaction; the caller
        /// rolls it back when the report is not <see cref="RoofReport.Ok"/> or when this throws.
        /// </summary>
        public static RoofReport Apply(Document doc, FootPrintRoof roof, RoofModel model, RoofSlopeSettings s)
        {
            RoofReport rep = new RoofReport();
            rep.Name = model.Name;

            if (roof == null || !roof.IsValidObject)
                throw new InvalidOperationException("The roof no longer exists.");

            SlabShapeEditor ed = roof.GetSlabShapeEditor();
            if (ed == null) throw new InvalidOperationException("This roof does not support shape editing.");

            if (s.ResetShape && ed.IsEnabled) { ed.ResetSlabShape(); doc.Regenerate(); }
            if (!ed.IsEnabled) { ed.Enable(); doc.Regenerate(); }

            List<SlabShapeVertex> verts = ed.SlabShapeVertices.Cast<SlabShapeVertex>().ToList();
            if (verts.Count == 0) throw new InvalidOperationException("Shape editing could not be enabled on this roof.");
            double z = verts[0].Position.Z;

            // corners Revit already created must not be duplicated
            model.Input.ExistingVertices = verts.Select(v => new P2(v.Position.X, v.Position.Y)).ToList();

            SolveResult res = SlopeSolver.Solve(model.Input, s);
            rep.Warnings.AddRange(res.Warnings);
            rep.Drains = res.DrainCount;
            if (res.Skipped) { rep.Ok = false; rep.Error = "no drain selected"; return rep; }

            // ---- 1. points ----
            foreach (SolvedPoint p in res.Points)
            {
                try
                {
                    SlabShapeVertex v = ed.AddPoint(new XYZ(p.P.X, p.P.Y, z));
                    if (v != null) rep.PointsAdded++; else rep.PointsFailed++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { rep.PointsFailed++; }
                catch (ArgumentException) { rep.PointsFailed++; }
            }
            doc.Regenerate();
            verts = ed.SlabShapeVertices.Cast<SlabShapeVertex>().ToList();

            // ---- 2. split lines (ridges / saddle ridges) ----
            double snap = Math.Max(s.MergeTolerance, 0.01) * 1.5;
            foreach (P2[] line in res.SplitLines)
            {
                SlabShapeVertex a = Nearest(verts, line[0], snap), b = Nearest(verts, line[1], snap);
                if (a == null || b == null || ReferenceEquals(a, b)) continue;
                try { ed.AddSplitLine(a, b); rep.SplitLines++; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { rep.SplitLinesFailed++; /* crossing or duplicate - the points still work */ }
                catch (ArgumentException) { rep.SplitLinesFailed++; }
            }
            if (rep.SplitLines > 0)
            {
                doc.Regenerate();
                verts = ed.SlabShapeVertices.Cast<SlabShapeVertex>().ToList();
            }

            // ---- 3. heights: every vertex (new and existing corners) gets slope x distance to its drain ----
            double max = 0;
            foreach (SlabShapeVertex v in verts)
            {
                double h = res.Field.At(new P2(v.Position.X, v.Position.Y));
                ed.ModifySubElement(v, h);
                max = Math.Max(max, h);
            }

            rep.RidgePoints = res.RidgePointCount;
            rep.MaxHeightMm = max * 304.8;
            rep.Ok = true;
            return rep;
        }

        private static SlabShapeVertex Nearest(List<SlabShapeVertex> verts, P2 p, double maxDist)
        {
            SlabShapeVertex best = null; double bd = maxDist;
            foreach (SlabShapeVertex v in verts)
            {
                double d = new P2(v.Position.X, v.Position.Y).DistanceTo(p);
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }
    }
}
