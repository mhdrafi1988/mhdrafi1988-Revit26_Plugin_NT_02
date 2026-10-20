using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace RoofPointsAndSlope.Tests
{
    /// <summary>Builds test roofs (feet, plan).</summary>
    internal static class TestShapes
    {
        public static Loop2D Rect(double x0, double y0, double x1, double y1) =>
            LoopChainer.Chain(new LoopSegment2D[]
            {
                new LineSegment2D(new P2(x0, y0), new P2(x1, y0)),
                new LineSegment2D(new P2(x1, y0), new P2(x1, y1)),
                new LineSegment2D(new P2(x1, y1), new P2(x0, y1)),
                new LineSegment2D(new P2(x0, y1), new P2(x0, y0)),
            }, 1e-6);

        public static Loop2D Circle(double cx, double cy, double r) =>
            LoopChainer.Chain(new[] { new ArcSegment2D(new P2(cx, cy), r, 0, 2 * System.Math.PI) }, 1e-6);

        /// <summary>A roof with one boundary, the given openings, and its corners as the existing points.</summary>
        public static RoofOutline Roof(Loop2D boundary, params Loop2D[] openings)
        {
            var outline = new RoofOutline();
            outline.Boundaries.Add(boundary);
            foreach (var s in boundary.Segments)
                outline.Edges.Add(new RoofEdge { Segment = s, Loop = boundary, IsBoundary = true });
            for (int i = 0; i < openings.Length; i++)
            {
                outline.Openings.Add(new OpeningModel { Index = i + 1, Loop = openings[i], PointCount = 4 });
                foreach (var s in openings[i].Segments)
                    outline.Edges.Add(new RoofEdge { Segment = s, Loop = openings[i], IsBoundary = false, OpeningIndex = i + 1 });
            }
            outline.ExistingVertices = outline.Edges.Select(e => e.Segment.Start).ToList();
            return outline;
        }

        /// <summary>All steps off.</summary>
        public static PipelineSettings Off() => new PipelineSettings
        {
            InnerPointsEnabled = false, PerpendicularEnabled = false, MidQuarterEnabled = false,
            GapFillEnabled = false, RidgeEnabled = false, SlopeEnabled = false, MinPointSpacingMm = 0
        }.Normalize();
    }
}
