namespace Revit26_Plugin_Revised.TrackerId.V002.Core.Models
{
    /// <summary>
    /// A grid line in plan, in Revit internal units (feet). Straight grids use the two end points;
    /// curved grids use the centre and radius of their arc.
    /// </summary>
    public sealed class GridLine2D
    {
        /// <summary>Grid name as shown in its bubble (e.g. "C" or "4").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>True for an arc grid.</summary>
        public bool IsCurved { get; set; }

        /// <summary>Start X (straight grids).</summary>
        public double X1 { get; set; }

        /// <summary>Start Y (straight grids).</summary>
        public double Y1 { get; set; }

        /// <summary>End X (straight grids).</summary>
        public double X2 { get; set; }

        /// <summary>End Y (straight grids).</summary>
        public double Y2 { get; set; }

        /// <summary>Arc centre X (curved grids).</summary>
        public double CenterX { get; set; }

        /// <summary>Arc centre Y (curved grids).</summary>
        public double CenterY { get; set; }

        /// <summary>Arc radius (curved grids).</summary>
        public double Radius { get; set; }
    }
}
