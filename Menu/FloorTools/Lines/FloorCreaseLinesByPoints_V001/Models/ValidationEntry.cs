using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Models
{
    /// <summary>
    /// Holds the equidistance validation result for a single generated ridge shape point.
    /// Each row maps to one row in the Excel validation report.
    /// </summary>
    public class ValidationEntry
    {
        /// <summary>Sequential point index in the output set.</summary>
        public int PointIndex { get; set; }

        /// <summary>X coordinate of the ridge point (Revit internal units — feet).</summary>
        public double X { get; set; }

        /// <summary>Y coordinate of the ridge point (Revit internal units — feet).</summary>
        public double Y { get; set; }

        /// <summary>
        /// Distances from this point to each contributing drain group centroid.
        /// Key = DrainGroup.GroupIndex, Value = distance in Revit internal units.
        /// </summary>
        public Dictionary<int, double> DistancesToGroups { get; set; } = new Dictionary<int, double>();

        /// <summary>Maximum deviation between any two distances in DistancesToGroups.</summary>
        public double MaxDeviation { get; set; }

        /// <summary>Tolerance used for the pass/fail check (Revit internal units).</summary>
        public double Tolerance { get; set; }

        /// <summary>True when MaxDeviation ≤ Tolerance.</summary>
        public bool Passed => MaxDeviation <= Tolerance;

        /// <summary>Optional human-readable note (e.g. "Boundary clip point", "Voronoi vertex").</summary>
        public string Note { get; set; } = string.Empty;
    }
}
