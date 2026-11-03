using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Services
{
    /// <summary>
    /// Groups drain points by 2D plan proximity using a Union-Find algorithm.
    /// Elevation (Z) is ignored — only X,Y distance is considered.
    /// Drains within ProximityDistanceFeet of each other are merged into one group.
    /// The group centroid becomes the Voronoi site.
    /// </summary>
    public class DrainGroupingService
    {
        // ── Union-Find internals ──────────────────────────────────────────────────
        private int[] _parent;
        private int[] _rank;

        private int Find(int i)
        {
            if (_parent[i] != i)
                _parent[i] = Find(_parent[i]); // path compression
            return _parent[i];
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return;
            if (_rank[ra] < _rank[rb]) { int tmp = ra; ra = rb; rb = tmp; }
            _parent[rb] = ra;
            if (_rank[ra] == _rank[rb]) _rank[ra]++;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Groups the supplied drain locations by 2D proximity.
        /// </summary>
        /// <param name="drainLocations">All drain XYZ points selected by the user.</param>
        /// <param name="proximityDistanceFeet">
        ///   Maximum 2D distance (Revit internal units = feet) for two drains to be in the same group.
        ///   Convert from mm before calling: feet = mm / 304.8
        /// </param>
        /// <returns>List of DrainGroup, each with Centroid computed.</returns>
        public List<DrainGroup> GroupDrains(List<XYZ> drainLocations, double proximityDistanceFeet)
        {
            int n = drainLocations.Count;
            _parent = new int[n];
            _rank   = new int[n];

            for (int i = 0; i < n; i++) { _parent[i] = i; _rank[i] = 0; }

            // Union any pair within proximity distance (2D)
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (FloorGeometry2D.Dist2D(drainLocations[i], drainLocations[j]) <= proximityDistanceFeet)
                        Union(i, j);
                }
            }

            // Collect groups
            var groupMap = new Dictionary<int, List<XYZ>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groupMap.ContainsKey(root))
                    groupMap[root] = new List<XYZ>();
                groupMap[root].Add(drainLocations[i]);
            }

            // Build DrainGroup objects
            var result = new List<DrainGroup>();
            int idx = 0;
            foreach (var kv in groupMap)
            {
                var dg = new DrainGroup
                {
                    GroupIndex     = idx++,
                    DrainLocations = kv.Value
                };
                dg.ComputeCentroid();
                result.Add(dg);
            }

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Converts millimetres to Revit internal units (feet).</summary>
        public static double MmToFeet(double mm) => mm / 304.8;
    }
}
