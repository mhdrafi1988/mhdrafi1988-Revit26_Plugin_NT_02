namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>
    /// One facet of a structural solid. The corners are ordered counter-clockwise seen from outside
    /// the solid, so <see cref="Normal"/> points out of it.
    /// </summary>
    public readonly struct Triangle
    {
        /// <summary>First corner.</summary>
        public Vec3 A { get; }

        /// <summary>Second corner.</summary>
        public Vec3 B { get; }

        /// <summary>Third corner.</summary>
        public Vec3 C { get; }

        /// <summary>Creates a triangle from its corners.</summary>
        public Triangle(Vec3 a, Vec3 b, Vec3 c)
        {
            A = a;
            B = b;
            C = c;
        }

        /// <summary>Unit normal by the right-hand rule (outward for a correctly wound solid).</summary>
        public Vec3 Normal => (B - A).Cross(C - A).Normalized();

        /// <summary>Bounding box of the three corners.</summary>
        public Aabb Bounds => Aabb.FromPoints(new[] { A, B, C });
    }
}
