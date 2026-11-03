namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Tolerances used by the 2D pattern engine. All lengths in metres.</summary>
    public static class GeometryTolerance
    {
        /// <summary>Numerical noise level for clipping and point coincidence.</summary>
        public const double Epsilon = 1e-9;

        /// <summary>Points closer than this are the same sketch vertex.</summary>
        public const double Vertex = 1e-6;

        /// <summary>Merge / de-duplication / on-line tolerance (1 mm).</summary>
        public const double Merge = 1e-3;

        /// <summary>Default minimum output segment length (1 mm).</summary>
        public const double MinSegmentLength = 1e-3;

        /// <summary>Loops with a smaller area are degenerate (m²).</summary>
        public const double MinLoopArea = 1e-4;

        /// <summary>Sine of the angle below which two directions are parallel.</summary>
        public const double ParallelSine = 1e-6;
    }
}
