// Reads a footprint roof's sketch (outer loop + openings) into the solver's RoofInput.
// The sketch is used rather than the top face so it works even when the roof is already shape-edited.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Core;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Infrastructure
{
    /// <summary>One selected roof: its id, display name, plan geometry and whether it is ticked in the window.</summary>
    public class RoofModel
    {
        public ElementId Id;
        public string Name;
        public double AreaM2;
        public bool Use = true;
        public RoofInput Input = new RoofInput();
        /// <summary>Note shown under the roof in the window (e.g. no Variable layer); null when none.</summary>
        public string Note;
    }

    /// <summary>Reads a footprint roof's sketch into a <see cref="RoofModel"/>.</summary>
    public static class RoofReader
    {
        private const double Ft2ToM2 = 0.09290304;

        private class Loop
        {
            public List<P2> Poly = new List<P2>();
            public List<P2> Corners = new List<P2>();
            public List<Curve> Curves = new List<Curve>();
        }

        /// <summary>
        /// Reads <paramref name="roof"/>. The largest loop is the outline, every other loop an opening
        /// (and a possible drain). Throws <see cref="InvalidOperationException"/> when no loop can be read.
        /// </summary>
        public static RoofModel Read(FootPrintRoof roof)
        {
            RoofModel model = new RoofModel();
            model.Id = roof.Id;
            model.Name = roof.Name + "  [" + roof.Id.Value + "]";

            List<Loop> loops = new List<Loop>();
            foreach (ModelCurveArray arr in roof.GetProfiles())
            {
                List<Curve> curves = new List<Curve>();
                foreach (ModelCurve mc in arr) curves.Add(mc.GeometryCurve);
                if (curves.Count == 0) continue;
                Loop loop = BuildLoop(Chain(curves));
                if (loop.Poly.Count >= 3) loops.Add(loop);
            }
            if (loops.Count == 0) throw new InvalidOperationException("Roof has no readable footprint.");

            // largest loop = outline, everything else = opening
            Loop outer = loops.OrderByDescending(l => Geo2D.Area(l.Poly)).First();
            RoofInput input = model.Input;
            input.Outer = outer.Poly;
            AddEdgesAndCurves(input, outer, -1);

            double area = Geo2D.Area(outer.Poly);
            int index = 0;
            foreach (Loop l in loops)
            {
                if (l == outer) continue;
                input.Inner.Add(l.Poly);
                input.InnerCorners.Add(l.Corners);
                AddEdgesAndCurves(input, l, index);

                P2 c = Geo2D.Centroid(l.Poly);
                DrainInfo d = new DrainInfo();
                d.LoopIndex = index;
                d.Center = c;
                d.Area = Geo2D.Area(l.Poly);
                d.Radius = l.Poly.Max(p => p.DistanceTo(c));
                d.Outline = l.Poly;
                input.Drains.Add(d);

                area -= d.Area;
                index++;
            }
            model.AreaM2 = area * Ft2ToM2;
            return model;
        }

        private static P2 To2(XYZ p) { return new P2(p.X, p.Y); }

        /// <summary>Orders the sketch curves head-to-tail (flipping where needed).</summary>
        private static List<Curve> Chain(List<Curve> curves)
        {
            if (curves.Count < 2 || curves.Any(c => !c.IsBound)) return curves;

            const double tol = 1e-4;
            List<Curve> rest = new List<Curve>(curves);
            List<Curve> ordered = new List<Curve> { rest[0] };
            rest.RemoveAt(0);
            XYZ end = ordered[0].GetEndPoint(1);

            while (rest.Count > 0)
            {
                int found = -1; bool flip = false;
                for (int i = 0; i < rest.Count; i++)
                {
                    if (rest[i].GetEndPoint(0).DistanceTo(end) < tol) { found = i; break; }
                    if (rest[i].GetEndPoint(1).DistanceTo(end) < tol) { found = i; flip = true; break; }
                }
                if (found < 0) { ordered.AddRange(rest); break; }   // broken chain - keep what is left as is
                Curve next = flip ? rest[found].CreateReversed() : rest[found];
                rest.RemoveAt(found);
                ordered.Add(next);
                end = next.GetEndPoint(1);
            }
            return ordered;
        }

        private static Loop BuildLoop(List<Curve> curves)
        {
            Loop loop = new Loop();
            loop.Curves = curves;
            foreach (Curve c in curves)
            {
                IList<XYZ> pts = c.Tessellate();
                for (int i = 0; i < pts.Count - 1; i++) loop.Poly.Add(To2(pts[i]));   // last point = next curve's first
                if (c.IsBound) loop.Corners.Add(To2(c.GetEndPoint(0)));
            }
            return loop;
        }

        private static void AddEdgesAndCurves(RoofInput input, Loop loop, int loopIndex)
        {
            foreach (Curve c in loop.Curves)
            {
                if (c is Line && c.IsBound)
                {
                    EdgeInfo e = new EdgeInfo();
                    e.LoopIndex = loopIndex;
                    e.A = To2(c.GetEndPoint(0));
                    e.B = To2(c.GetEndPoint(1));
                    input.StraightEdges.Add(e);
                }
                else
                {
                    Curve curve = c;   // captured below
                    CurveInfo ci = new CurveInfo();
                    ci.LoopIndex = loopIndex;
                    ci.Length = curve.Length;
                    ci.Eval = delegate(double t)
                    {
                        return curve.IsBound
                            ? To2(curve.Evaluate(t, true))
                            : To2(curve.Evaluate(t * curve.Period, false));
                    };
                    input.Curves.Add(ci);
                }
            }
        }
    }
}
