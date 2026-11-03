using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Services
{
    /// <summary>
    /// Validates that every generated ridge shape point satisfies the equidistance
    /// condition relative to its contributing drain groups.
    ///
    /// For each point:
    ///   d_i = dist2D(point, groupCentroid_i)
    ///   MaxDeviation = max(d_i) - min(d_i)
    ///   Pass  →  MaxDeviation ≤ tolerance
    ///
    /// Populates VoronoiRidgeResult.ValidationLog and updates PassCount / FailCount.
    /// No Revit transaction required.
    /// </summary>
    public class RidgeValidationService
    {
        /// <summary>
        /// Tolerance in Revit internal units (feet).
        /// Default: 5 mm ≈ 0.01640 ft.
        /// </summary>
        public double ToleranceFeet { get; set; } = 5.0 / 304.8;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs validation for all shape points in <paramref name="result"/>
        /// and writes a ValidationEntry per point into result.ValidationLog.
        /// </summary>
        public void Validate(VoronoiRidgeResult result, List<DrainGroup> groups)
        {
            result.ValidationLog.Clear();
            result.PassCount = 0;
            result.FailCount = 0;

            // Build quick lookup: GroupIndex → Centroid
            var centroids = new Dictionary<int, XYZ>();
            foreach (var g in groups)
                centroids[g.GroupIndex] = g.Centroid;

            for (int i = 0; i < result.ShapePoints.Count; i++)
            {
                XYZ pt = result.ShapePoints[i];

                // Retrieve contributing group indices for this point
                List<int> groupIndices;
                if (!result.ShapePointGroupMap.TryGetValue(i, out groupIndices)
                    || groupIndices == null || groupIndices.Count == 0)
                {
                    // No mapping — find two nearest groups as fallback
                    groupIndices = FloorGeometry2D.FindNNearestGroups(pt, groups, 2);
                }

                var entry = new ValidationEntry
                {
                    PointIndex = i,
                    X          = pt.X,
                    Y          = pt.Y,
                    Tolerance  = ToleranceFeet
                };

                double minDist = double.MaxValue;
                double maxDist = double.MinValue;

                foreach (int gi in groupIndices)
                {
                    if (!centroids.TryGetValue(gi, out XYZ centroid)) continue;
                    double d = FloorGeometry2D.Dist2D(pt, centroid);
                    entry.DistancesToGroups[gi] = d;
                    if (d < minDist) minDist = d;
                    if (d > maxDist) maxDist = d;
                }

                entry.MaxDeviation = (maxDist == double.MinValue) ? 0 : maxDist - minDist;

                if (entry.Passed)
                    result.PassCount++;
                else
                    result.FailCount++;

                result.ValidationLog.Add(entry);
            }
        }
    }
}
