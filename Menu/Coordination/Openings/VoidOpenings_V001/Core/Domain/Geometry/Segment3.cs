namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>Straight piece of an MEP centreline, from <see cref="Start"/> to <see cref="End"/>.</summary>
    public readonly struct Segment3
    {
        /// <summary>Start point.</summary>
        public Vec3 Start { get; }

        /// <summary>End point.</summary>
        public Vec3 End { get; }

        /// <summary>Creates a segment between two points.</summary>
        public Segment3(Vec3 start, Vec3 end)
        {
            Start = start;
            End = end;
        }

        /// <summary>Length of the segment.</summary>
        public double Length => (End - Start).Length;

        /// <summary>Unit direction from start to end (zero for a degenerate segment).</summary>
        public Vec3 Direction => (End - Start).Normalized();

        /// <summary>Point at parameter <paramref name="t"/>: 0 is the start, 1 the end.</summary>
        public Vec3 PointAt(double t) => Start + (End - Start) * t;

        /// <summary>The same segment moved sideways by <paramref name="offset"/>.</summary>
        public Segment3 Offset(Vec3 offset) => new Segment3(Start + offset, End + offset);

        /// <summary>Bounding box of the two end points.</summary>
        public Aabb Bounds => Aabb.FromPoints(new[] { Start, End });
    }
}
