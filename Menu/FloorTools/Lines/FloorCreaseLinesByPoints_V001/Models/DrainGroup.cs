using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Models
{
    /// <summary>
    /// Represents a group of drain points that are within the proximity threshold of each other.
    /// The Centroid is used as the Voronoi site for this group.
    /// </summary>
    public class DrainGroup
    {
        /// <summary>Zero-based index assigned during grouping.</summary>
        public int GroupIndex { get; set; }

        /// <summary>All drain XYZ locations belonging to this group.</summary>
        public List<XYZ> DrainLocations { get; set; } = new List<XYZ>();

        /// <summary>
        /// 2D plan centroid (X, Y average of all drains in group; Z = 0).
        /// Used as the Voronoi site coordinate.
        /// </summary>
        public XYZ Centroid { get; private set; }

        /// <summary>Recomputes the centroid from current DrainLocations. Call after all drains are added.</summary>
        public void ComputeCentroid()
        {
            if (DrainLocations.Count == 0)
            {
                Centroid = XYZ.Zero;
                return;
            }

            double sumX = 0, sumY = 0;
            foreach (var pt in DrainLocations)
            {
                sumX += pt.X;
                sumY += pt.Y;
            }

            Centroid = new XYZ(sumX / DrainLocations.Count, sumY / DrainLocations.Count, 0);
        }
    }
}
