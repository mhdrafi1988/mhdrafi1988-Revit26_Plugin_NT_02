namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// The region { p : Normal·p + Offset ≥ 0 }. The normal is stored as a unit
    /// vector so <see cref="Evaluate"/> returns a signed distance in metres.
    /// </summary>
    public readonly struct HalfPlane
    {
        public HalfPlane(Vec2 normal, double offset)
        {
            double len = normal.Length;
            if (len < 1e-15)
            {
                Normal = Vec2.Zero;
                Offset = offset;
            }
            else
            {
                Normal = normal / len;
                Offset = offset / len;
            }
        }

        public Vec2 Normal { get; }
        public double Offset { get; }

        /// <summary>A half-plane with a zero normal is either everything or nothing.</summary>
        public bool IsDegenerate => Normal.Length < 0.5;

        /// <summary>Signed distance: ≥ 0 inside.</summary>
        public double Evaluate(Vec2 p) => Normal.Dot(p) + Offset;

        /// <summary>Half-plane on the side the normal points to, bounded by the line through <paramref name="point"/>.</summary>
        public static HalfPlane FromPointNormal(Vec2 point, Vec2 normal) =>
            new HalfPlane(normal, -normal.Dot(point));
    }
}
