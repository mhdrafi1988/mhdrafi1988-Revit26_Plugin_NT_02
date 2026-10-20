namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>
    /// A scope box seen in plan: a rectangle that may be rotated. A point is inside when its
    /// projections on the box's two axes (<see cref="UX"/>,<see cref="UY"/>) and the axis at
    /// right angles to it fall within the stored ranges. Revit internal units (feet).
    /// </summary>
    public sealed class ZoneBox
    {
        /// <summary>Scope box name, used as the zone value.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>X component of the box's first axis (unit length).</summary>
        public double UX { get; set; } = 1;

        /// <summary>Y component of the box's first axis (unit length).</summary>
        public double UY { get; set; }

        /// <summary>Smallest projection on the first axis.</summary>
        public double MinU { get; set; }

        /// <summary>Largest projection on the first axis.</summary>
        public double MaxU { get; set; }

        /// <summary>Smallest projection on the second axis.</summary>
        public double MinV { get; set; }

        /// <summary>Largest projection on the second axis.</summary>
        public double MaxV { get; set; }

        /// <summary>Plan area, used to prefer the smallest box when boxes overlap.</summary>
        public double Area => (MaxU - MinU) * (MaxV - MinV);

        /// <summary>True when the plan point (x, y) lies inside the box (edges included).</summary>
        public bool Contains(double x, double y)
        {
            double u = x * UX + y * UY;
            double v = -x * UY + y * UX;
            const double eps = 1e-6;
            return u >= MinU - eps && u <= MaxU + eps && v >= MinV - eps && v <= MaxV + eps;
        }
    }
}
