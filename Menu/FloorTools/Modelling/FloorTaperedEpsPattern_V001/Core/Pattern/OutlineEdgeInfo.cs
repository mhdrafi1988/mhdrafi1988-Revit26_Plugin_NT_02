using System;
using System.Collections.Generic;
using System.Globalization;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>One edge of the validated outline, described for the alignment-edge picker.</summary>
    public sealed class OutlineEdgeInfo
    {
        public OutlineEdgeInfo(int index, double lengthM, double angleDeg, bool isLongest)
        {
            Index = index;
            LengthM = lengthM;
            AngleDeg = angleDeg;
            IsLongest = isLongest;
        }

        /// <summary>0-based index in CCW order (after collinear merge).</summary>
        public int Index { get; }
        public double LengthM { get; }

        /// <summary>Direction of the edge line from project X, 0 ≤ angle &lt; 180°.</summary>
        public double AngleDeg { get; }
        public bool IsLongest { get; }

        public string Label => string.Format(CultureInfo.CurrentCulture,
            "Edge {0} — {1:0.00} m @ {2:0.0}°{3}", Index + 1, LengthM, AngleDeg, IsLongest ? "  (longest)" : string.Empty);

        public static IReadOnlyList<OutlineEdgeInfo> Describe(Polygon2D outlineCcw)
        {
            var list = new List<OutlineEdgeInfo>(outlineCcw.Count);
            if (outlineCcw.IsEmpty)
            {
                return list;
            }

            int longest = GridFrame.LongestEdgeIndex(outlineCcw);
            for (int i = 0; i < outlineCcw.Count; i++)
            {
                Segment2D e = outlineCcw.Edge(i);
                Vec2 d = e.Direction;
                double angle = Math.Atan2(d.Y, d.X) * 180.0 / Math.PI;
                angle = ((angle % 180.0) + 180.0) % 180.0;
                if (angle > 179.95)
                {
                    angle = 0;
                }

                list.Add(new OutlineEdgeInfo(i, e.Length, angle, i == longest));
            }

            return list;
        }

        public override string ToString() => Label;
    }
}
