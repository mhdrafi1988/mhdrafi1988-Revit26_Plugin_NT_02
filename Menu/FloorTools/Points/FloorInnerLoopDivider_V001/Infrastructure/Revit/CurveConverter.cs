// =======================================================
// File: CurveConverter.cs
// Location: Infrastructure/Revit/
// New in V012. Revit Curve → plan segments for Core/Geometry.
//   Line  → LineSegment2D
//   Arc   → ArcSegment2D (bound arcs via start/mid/end so direction and
//           sweeps over 180° are right; unbound = full circle)
//   other → tessellated into line pieces (ellipses, splines …)
// Z is dropped — the writer projects every new point back onto the
// floor's top surface.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Infrastructure.Revit
{
    public static class CurveConverter
    {
        public static P2 ToP2(XYZ p) => new P2(p.X, p.Y);

        public static IEnumerable<LoopSegment2D> ToSegments(Curve curve)
        {
            if (curve == null) yield break;

            if (curve is Line line)
            {
                yield return new LineSegment2D(ToP2(line.GetEndPoint(0)), ToP2(line.GetEndPoint(1)));
                yield break;
            }

            if (curve is Arc arc)
            {
                P2 c = ToP2(arc.Center);
                if (!arc.IsBound)
                {
                    double a0 = Math.Atan2(arc.XDirection.Y, arc.XDirection.X);
                    double sweep = arc.Normal.Z >= 0 ? 2 * Math.PI : -2 * Math.PI;
                    yield return new ArcSegment2D(c, arc.Radius, a0, sweep);
                    yield break;
                }

                ArcSegment2D seg = ArcSegment2D.FromThreePoints(
                    c, arc.Radius,
                    ToP2(arc.GetEndPoint(0)),
                    ToP2(arc.Evaluate(0.5, true)),
                    ToP2(arc.GetEndPoint(1)));
                if (seg != null)
                {
                    yield return seg;
                    yield break;
                }
            }

            IList<XYZ> pts = curve.Tessellate();
            for (int i = 0; i + 1 < pts.Count; i++)
                yield return new LineSegment2D(ToP2(pts[i]), ToP2(pts[i + 1]));
        }
    }
}
