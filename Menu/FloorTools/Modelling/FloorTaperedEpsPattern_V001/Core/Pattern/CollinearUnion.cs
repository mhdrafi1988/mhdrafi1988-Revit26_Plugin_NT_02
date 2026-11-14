using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Joins segments that lie on the same line and overlap or touch into one segment,
    /// regardless of other lines meeting there (unlike <see cref="SegmentMerger"/>).
    /// Used to turn board edges into continuous grid lines.
    /// </summary>
    public static class CollinearUnion
    {
        private sealed class LineGroup
        {
            public LineGroup(Vec2 origin, Vec2 direction)
            {
                Origin = origin;
                Direction = direction;
            }

            public Vec2 Origin { get; }
            public Vec2 Direction { get; }
            public List<(double T0, double T1)> Intervals { get; } = new List<(double, double)>();
        }

        public static IReadOnlyList<Segment2D> Union(IEnumerable<Segment2D> segments, double tolerance)
        {
            var groups = new List<LineGroup>();
            foreach (Segment2D s in segments)
            {
                if (s.Length < tolerance)
                {
                    continue;
                }

                Vec2 d = Canonical(s.Direction);
                LineGroup? g = groups.FirstOrDefault(x =>
                    Math.Abs(x.Direction.Cross(d)) < GeometryTolerance.ParallelSine &&
                    Math.Abs(x.Direction.Cross(s.Start - x.Origin)) < tolerance);
                if (g == null)
                {
                    g = new LineGroup(s.Start, d);
                    groups.Add(g);
                }

                double t0 = (s.Start - g.Origin).Dot(g.Direction);
                double t1 = (s.End - g.Origin).Dot(g.Direction);
                g.Intervals.Add((Math.Min(t0, t1), Math.Max(t0, t1)));
            }

            var result = new List<Segment2D>();
            foreach (LineGroup g in groups)
            {
                List<(double T0, double T1)> sorted = g.Intervals.OrderBy(i => i.T0).ToList();
                double a = sorted[0].T0, b = sorted[0].T1;
                foreach ((double t0, double t1) in sorted.Skip(1))
                {
                    if (t0 <= b + tolerance)
                    {
                        b = Math.Max(b, t1);
                    }
                    else
                    {
                        result.Add(new Segment2D(g.Origin + g.Direction * a, g.Origin + g.Direction * b));
                        a = t0;
                        b = t1;
                    }
                }

                result.Add(new Segment2D(g.Origin + g.Direction * a, g.Origin + g.Direction * b));
            }

            return result;
        }

        private static Vec2 Canonical(Vec2 d) =>
            d.X > GeometryTolerance.ParallelSine || (Math.Abs(d.X) <= GeometryTolerance.ParallelSine && d.Y > 0) ? d : -d;
    }
}
