// IslandRoofSplitter V002 — snaps line endpoints that are within the healing tolerance (1 mm) onto one point.
// Larger gaps are left open on purpose; GraphCleaner reports them as Skipped (unclosed).
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Services
{
    public static class EndpointHealer
    {
        /// <returns>Number of endpoints that actually moved (gaps healed).</returns>
        public static int Heal(List<TessellatedSource> sources, double healTolFt)
        {
            // endpoint refs: (source index, isLast)
            var refs = new List<(int src, bool last)>();
            foreach (var (s, i) in sources.Select((s, i) => (s, i)))
            {
                if (s.Points.Count < 2) continue;
                refs.Add((i, false));
                refs.Add((i, true));
            }
            Pt2 At((int src, bool last) r) => r.last ? sources[r.src].Points[^1] : sources[r.src].Points[0];

            var ds = new DisjointSet(refs.Count);
            var cells = new Dictionary<(long, long), List<int>>();
            double cs = Math.Max(healTolFt, 1e-9);
            for (int k = 0; k < refs.Count; k++)
            {
                var p = At(refs[k]);
                long cx = (long)Math.Floor(p.X / cs), cy = (long)Math.Floor(p.Y / cs);
                for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (!cells.TryGetValue((cx + dx, cy + dy), out var list)) continue;
                    foreach (int other in list)
                        if (At(refs[other]).DistanceTo(p) <= healTolFt) ds.Union(k, other);
                }
                if (!cells.TryGetValue((cx, cy), out var cell)) cells[(cx, cy)] = cell = new List<int>();
                cell.Add(k);
            }

            int moved = 0;
            foreach (var group in Enumerable.Range(0, refs.Count).GroupBy(ds.Find))
            {
                var members = group.ToList();
                if (members.Count < 2) continue;
                double x = members.Average(m => At(refs[m]).X);
                double y = members.Average(m => At(refs[m]).Y);
                var target = new Pt2(x, y);
                foreach (int m in members)
                {
                    var r = refs[m];
                    var pts = sources[r.src].Points;
                    int idx = r.last ? pts.Count - 1 : 0;
                    if (pts[idx].DistanceTo(target) > 1e-9) moved++;
                    pts[idx] = target;
                }
            }
            return moved;
        }
    }
}
