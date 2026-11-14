using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>
    /// Axis-aligned bounding box. Used only as the fast first filter; a clash is never decided from
    /// boxes alone.
    /// </summary>
    public readonly struct Aabb
    {
        /// <summary>Corner with the smallest coordinates.</summary>
        public Vec3 Min { get; }

        /// <summary>Corner with the largest coordinates.</summary>
        public Vec3 Max { get; }

        /// <summary>Creates a box from its two extreme corners.</summary>
        public Aabb(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>Smallest box holding every point. Throws when there are no points.</summary>
        public static Aabb FromPoints(IEnumerable<Vec3> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool any = false;
            foreach (Vec3 p in points)
            {
                any = true;
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
            if (!any) throw new ArgumentException("A bounding box needs at least one point.", nameof(points));
            return new Aabb(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));
        }

        /// <summary>The box grown by <paramref name="margin"/> on every side.</summary>
        public Aabb Expand(double margin)
        {
            var m = new Vec3(margin, margin, margin);
            return new Aabb(Min - m, Max + m);
        }

        /// <summary>True when the boxes overlap or touch.</summary>
        public bool Intersects(Aabb other)
            => Min.X <= other.Max.X && Max.X >= other.Min.X
            && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y
            && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    }
}
