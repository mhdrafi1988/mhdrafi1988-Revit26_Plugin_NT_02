// IslandRoofSplitter V002 — turns each SourceSegment into a plan polyline.
// Arcs are split at a chord (sagitta) tolerance and keep their ArcInfo so Flat mode can rebuild true arcs.
#nullable enable
using System;
using System.Collections.Generic;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Services
{
    public sealed class TessellatedSource
    {
        public long SourceId { get; init; }
        public List<Pt2> Points { get; init; } = new();
        /// <summary>Index into the shared arc list, or -1 for straight / polyline sources.</summary>
        public int ArcIndex { get; init; } = -1;
    }

    public static class CurveTessellator
    {
        /// <param name="chordTolFt">Max distance between the true arc and a chord.</param>
        public static List<TessellatedSource> Tessellate(IEnumerable<SourceSegment> segments, double chordTolFt,
                                                         List<ArcInfo> arcsOut)
        {
            var result = new List<TessellatedSource>();
            foreach (var seg in segments)
            {
                if (seg.Points == null || seg.Points.Count < 2) continue;

                if (seg.Kind == SegmentKind.Arc && seg.Points.Count == 3 &&
                    TryArc(seg.Points[0], seg.Points[1], seg.Points[2], out var arc))
                {
                    arcsOut.Add(arc);
                    result.Add(new TessellatedSource
                    {
                        SourceId = seg.SourceId,
                        ArcIndex = arcsOut.Count - 1,
                        Points = SampleArc(arc, seg.Points[0], seg.Points[2], chordTolFt)
                    });
                }
                else if (seg.Kind == SegmentKind.Arc)
                {
                    // degenerate (collinear) arc → straight line start→end
                    result.Add(new TessellatedSource { SourceId = seg.SourceId, Points = new List<Pt2> { seg.Points[0], seg.Points[^1] } });
                }
                else
                {
                    result.Add(new TessellatedSource { SourceId = seg.SourceId, Points = new List<Pt2>(seg.Points) });
                }
            }
            return result;
        }

        public static bool TryArc(Pt2 start, Pt2 mid, Pt2 end, out ArcInfo arc)
        {
            arc = null!;
            bool closed = start.DistanceTo(end) < 1e-9;
            Pt2 c;
            double r;
            if (closed)
            {
                // full circle given as start==end with a point opposite
                c = new Pt2((start.X + mid.X) / 2, (start.Y + mid.Y) / 2);
                r = c.DistanceTo(start);
                if (r < 1e-9) return false;
                double a0c = Math.Atan2(start.Y - c.Y, start.X - c.X);
                arc = new ArcInfo { Center = c, Radius = r, StartAngle = a0c, Sweep = 2 * Math.PI };
                return true;
            }
            if (!Geometry2D.Circumcircle(start, mid, end, out c, out r)) return false;

            double a0 = Math.Atan2(start.Y - c.Y, start.X - c.X);
            double a1 = Math.Atan2(end.Y - c.Y, end.X - c.X);
            bool ccw = Pt2.Cross(mid - start, end - mid) > 0;
            double sweep = ccw ? Geometry2D.NormalizePositive(a1 - a0) : -Geometry2D.NormalizePositive(a0 - a1);
            arc = new ArcInfo { Center = c, Radius = r, StartAngle = a0, Sweep = sweep };
            return true;
        }

        public static int SegmentCount(ArcInfo arc, double chordTolFt)
        {
            double maxStep = chordTolFt >= arc.Radius
                ? Math.PI / 2
                : Math.Min(Math.PI / 2, 2 * Math.Acos(1 - chordTolFt / arc.Radius));
            return Math.Max(2, (int)Math.Ceiling(Math.Abs(arc.Sweep) / maxStep));
        }

        private static List<Pt2> SampleArc(ArcInfo arc, Pt2 exactStart, Pt2 exactEnd, double chordTolFt)
        {
            int n = SegmentCount(arc, chordTolFt);
            var pts = new List<Pt2>(n + 1) { exactStart };
            for (int i = 1; i < n; i++)
                pts.Add(arc.PointAt(arc.StartAngle + arc.Sweep * i / n));
            pts.Add(exactEnd);
            return pts;
        }
    }
}
