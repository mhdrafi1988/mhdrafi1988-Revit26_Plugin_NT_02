using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// A single linked-model element considered as a candidate for edge matching,
    /// already transformed into host-document space.
    /// </summary>
    public class LinkedElementCandidate
    {
        public long ElementId { get; set; }
        public long LinkInstanceId { get; set; }
        public string CategoryName { get; set; }
        public XYZ HostSpacePosition { get; set; }
    }

    /// <summary>
    /// One surviving (or capped) cluster of same-category candidates found near one
    /// bucketed floor edge — the unit that becomes a single PlannedSection row.
    /// </summary>
    public class ElementMatch
    {
        public EdgeDirection Direction { get; set; }
        public string CategoryName { get; set; }
        public XYZ RepresentativePoint { get; set; }
        public List<(long LinkInstanceId, long ElementId)> MemberRefs { get; set; } = new();
        public int Count => MemberRefs.Count;

        /// <summary>True when this cluster exceeded the 2-clusters-per-(direction,category) cap.</summary>
        public bool ClusterCapped { get; set; }
    }

    /// <summary>
    /// Matches linked elements to the nearest bucketed floor edge within a tolerance,
    /// then single-linkage clusters same-category matches along the edge parameter,
    /// capping at 2 clusters per (direction, category) group.
    /// </summary>
    public static class ElementEdgeMatchingEngine
    {
        public static List<ElementMatch> MatchAndCluster(
            Dictionary<EdgeDirection, FloorEdgeBucketingEngine.BucketedEdge> bucketedEdges,
            IEnumerable<LinkedElementCandidate> candidates,
            double edgeToleranceFeet,
            double dedupToleranceFeet,
            XYZ floorCenter,
            IList<LogEntry> log)
        {
            var results = new List<ElementMatch>();
            if (bucketedEdges.Count == 0)
                return results;

            // Step 1: for each candidate, find the nearest bucketed edge and keep it
            // only if within edgeToleranceFeet.
            var matched = new List<(LinkedElementCandidate Candidate, EdgeDirection Direction, double Param)>();
            int discardedTooFar = 0;

            foreach (var cand in candidates)
            {
                EdgeDirection? bestDir = null;
                double bestDist = double.MaxValue;
                double bestParam = 0;

                foreach (var kvp in bucketedEdges)
                {
                    Curve curve = kvp.Value.Curve;
                    IntersectionResult ir;
                    try
                    {
                        ir = curve.Project(cand.HostSpacePosition);
                    }
                    catch
                    {
                        continue;
                    }
                    if (ir == null) continue;

                    double dist = ir.Distance;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestDir = kvp.Key;
                        bestParam = ir.Parameter;
                    }
                }

                if (bestDir.HasValue && bestDist <= edgeToleranceFeet)
                {
                    matched.Add((cand, bestDir.Value, bestParam));
                }
                else
                {
                    discardedTooFar++;
                }
            }

            if (discardedTooFar > 0)
            {
                log.Add(new LogEntry(LogLevel.Info,
                    $"{discardedTooFar} linked element(s) discarded — beyond edge proximity tolerance for this floor."));
            }

            // Step 2: group by (Direction, CategoryName).
            var groups = matched.GroupBy(m => (m.Direction, m.Candidate.CategoryName));

            foreach (var group in groups)
            {
                var ordered = group.OrderBy(m => m.Param).ToList();

                // Step 3: single-linkage clustering along the 1D edge parameter.
                var clusters = new List<List<(LinkedElementCandidate Candidate, EdgeDirection Direction, double Param)>>();
                List<(LinkedElementCandidate Candidate, EdgeDirection Direction, double Param)> current = null;
                double? lastParam = null;

                foreach (var item in ordered)
                {
                    if (current == null)
                    {
                        current = new List<(LinkedElementCandidate, EdgeDirection, double)> { item };
                    }
                    else if (Math.Abs(item.Param - lastParam.Value) <= dedupToleranceFeet)
                    {
                        current.Add(item);
                    }
                    else
                    {
                        clusters.Add(current);
                        current = new List<(LinkedElementCandidate, EdgeDirection, double)> { item };
                    }
                    lastParam = item.Param;
                }
                if (current != null)
                    clusters.Add(current);

                // Step 4: cap at 2 clusters per group — keep the 2 with the most
                // elements (ties broken by position along the edge, first two win).
                List<List<(LinkedElementCandidate Candidate, EdgeDirection Direction, double Param)>> kept;
                List<List<(LinkedElementCandidate Candidate, EdgeDirection Direction, double Param)>> capped;

                if (clusters.Count > 2)
                {
                    var indexed = clusters.Select((c, idx) => (Cluster: c, Idx: idx)).ToList();
                    var keptIndexed = indexed
                        .OrderByDescending(x => x.Cluster.Count)
                        .ThenBy(x => x.Idx)
                        .Take(2)
                        .OrderBy(x => x.Idx)
                        .ToList();

                    var keptIdxSet = keptIndexed.Select(x => x.Idx).ToHashSet();
                    kept = keptIndexed.Select(x => x.Cluster).ToList();
                    capped = indexed.Where(x => !keptIdxSet.Contains(x.Idx)).Select(x => x.Cluster).ToList();
                }
                else
                {
                    kept = clusters;
                    capped = new List<List<(LinkedElementCandidate Candidate, EdgeDirection Direction, double Param)>>();
                }

                foreach (var cluster in kept)
                {
                    results.Add(BuildMatch(cluster, group.Key.Direction, group.Key.CategoryName,
                        bucketedEdges[group.Key.Direction].Curve, false));
                }

                foreach (var cluster in capped)
                {
                    results.Add(BuildMatch(cluster, group.Key.Direction, group.Key.CategoryName,
                        bucketedEdges[group.Key.Direction].Curve, true));

                    log.Add(new LogEntry(LogLevel.Warning,
                        $"{group.Key.CategoryName} on {group.Key.Direction}: extra cluster ({cluster.Count} element(s)) exceeds the 2-per-side cap — capped, not created."));
                }
            }

            return results;
        }

        private static ElementMatch BuildMatch(
            List<(LinkedElementCandidate Candidate, EdgeDirection Direction, double Param)> cluster,
            EdgeDirection direction,
            string categoryName,
            Curve curve,
            bool capped)
        {
            double avgParam = cluster.Average(c => c.Param);
            double t0 = curve.GetEndParameter(0);
            double t1 = curve.GetEndParameter(1);
            double clamped = Math.Min(Math.Max(avgParam, Math.Min(t0, t1)), Math.Max(t0, t1));

            XYZ representative;
            try
            {
                representative = curve.Evaluate(clamped, false);
            }
            catch
            {
                // Fallback: average of member positions if the curve can't be evaluated
                // at the clamped parameter for some reason (defensive — should not happen
                // for the Line/Arc edges FloorEdgeBucketingEngine produces).
                var pts = cluster.Select(c => c.Candidate.HostSpacePosition).ToList();
                representative = new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
            }

            return new ElementMatch
            {
                Direction = direction,
                CategoryName = categoryName,
                RepresentativePoint = representative,
                MemberRefs = cluster.Select(c => (c.Candidate.LinkInstanceId, c.Candidate.ElementId)).ToList(),
                ClusterCapped = capped
            };
        }
    }
}
