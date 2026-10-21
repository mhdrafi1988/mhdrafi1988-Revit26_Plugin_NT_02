using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry
{
    /// <summary>
    /// What the tool knows about one element's shape, in project coordinates (feet): its plan hull,
    /// the smallest rectangle around it, and its height range. No Revit API dependency.
    /// </summary>
    public sealed class ElementFootprint
    {
        private ElementFootprint(IReadOnlyList<Vec2> hull, PlanRectangle rectangle, double minZ, double maxZ)
        {
            Hull = hull;
            Rectangle = rectangle;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        /// <summary>Convex hull of the element in plan, counter-clockwise.</summary>
        public IReadOnlyList<Vec2> Hull { get; }

        /// <summary>Smallest plan rectangle around the element; its long sides are the element's largest sides.</summary>
        public PlanRectangle Rectangle { get; }

        /// <summary>Lowest point of the element.</summary>
        public double MinZ { get; }

        /// <summary>Highest point of the element.</summary>
        public double MaxZ { get; }

        /// <summary>Height of the element.</summary>
        public double Height => MaxZ - MinZ;

        /// <summary>Area of one long side: rectangle length × height (ft²).</summary>
        public double LongSideArea => Rectangle.Length * Height;

        /// <summary>Area of one short side: rectangle width × height (ft²).</summary>
        public double ShortSideArea => Rectangle.Width * Height;

        /// <summary>Builds the footprint from the element's geometry points, or returns null when there are none.</summary>
        public static ElementFootprint FromPoints(IEnumerable<Point3> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var plan = new List<Vec2>();
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (Point3 p in points)
            {
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z)) continue;
                plan.Add(p.Plan);
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
            }
            if (plan.Count == 0) return null;

            IReadOnlyList<Vec2> hull = PlanGeometry.ConvexHull(plan);
            return new ElementFootprint(hull, PlanGeometry.MinAreaRectangle(hull), minZ, maxZ);
        }
    }
}
