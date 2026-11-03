using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Turns raw footprint sketch loops (metres) into a single convex CCW outline with
    /// collinear sketch segments merged. Reports problems as messages — never throws.
    /// </summary>
    public static class OutlineValidator
    {
        public const string ConcaveMessage =
            "The floor outline is concave — split the floor into convex parts.";

        public static OutlineValidationResult Validate(IReadOnlyList<IReadOnlyList<Segment2D>> loops, int nonLinearCurveCount = 0)
        {
            var errors = new List<string>();

            if (loops == null || loops.Count == 0)
            {
                return Fail("The floor footprint has no sketch loop.", "Invalid");
            }

            if (loops.Count > 1)
            {
                errors.Add($"The floor footprint has {loops.Count} loops. Openings are not supported — use a single outer loop.");
            }

            if (nonLinearCurveCount > 0)
            {
                errors.Add($"The floor footprint has {nonLinearCurveCount} curved segment(s). Only straight lines are supported.");
            }

            if (errors.Count > 0)
            {
                return new OutlineValidationResult { Errors = errors, ShapeName = "Unsupported" };
            }

            IReadOnlyList<Vec2>? chained = LoopChainer.Chain(loops[0], GeometryTolerance.Merge);
            if (chained == null)
            {
                return Fail("The floor footprint loop is not closed.", "Invalid");
            }

            Polygon2D polygon = new Polygon2D(chained).Cleaned(GeometryTolerance.Vertex);
            if (polygon.IsEmpty || polygon.Area < GeometryTolerance.MinLoopArea)
            {
                return Fail("The floor footprint is degenerate (zero area).", "Invalid");
            }

            polygon = polygon.ToCounterClockwise();
            if (!polygon.IsConvex(GeometryTolerance.ParallelSine))
            {
                return Fail(ConcaveMessage, "Concave");
            }

            return new OutlineValidationResult { Outline = polygon, ShapeName = DescribeShape(polygon) };
        }

        public static string DescribeShape(Polygon2D convexCcw)
        {
            int n = convexCcw.Count;
            if (n == 3)
            {
                return "Triangle";
            }

            if (n == 4)
            {
                bool rightAngles = Enumerable.Range(0, 4).All(i =>
                    Math.Abs(convexCcw.Edge(i).Direction.Dot(convexCcw.Edge((i + 1) % 4).Direction)) < 1e-6);
                if (rightAngles)
                {
                    return Math.Abs(convexCcw.Edge(0).Length - convexCcw.Edge(1).Length) < GeometryTolerance.Merge
                        ? "Square"
                        : "Rectangle";
                }

                return "Convex quadrilateral";
            }

            return $"Convex {n}-gon";
        }

        private static OutlineValidationResult Fail(string message, string shape) =>
            new OutlineValidationResult { Errors = new[] { message }, ShapeName = shape };
    }
}
