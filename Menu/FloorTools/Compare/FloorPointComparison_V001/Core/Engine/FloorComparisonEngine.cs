using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Engine
{
    /// <summary>
    /// Compares two lists of floor shape-editing points by position (XY, within
    /// positionToleranceMm) and, for matched positions, by elevation (within
    /// elevationToleranceMm). Produces the entry list used for markers and the
    /// rolled-up ComparisonMetrics used by the Metrics Card.
    /// </summary>
    public static class FloorComparisonEngine
    {
        public static ComparisonResult Compare(
            List<FloorPointSample> pointsA,
            List<FloorPointSample> pointsB,
            double positionToleranceMm,
            double elevationToleranceMm)
        {
            pointsA = pointsA ?? new List<FloorPointSample>();
            pointsB = pointsB ?? new List<FloorPointSample>();

            double positionToleranceFt = UnitUtils.ConvertToInternalUnits(positionToleranceMm, UnitTypeId.Millimeters);

            var entries = new List<PointComparisonEntry>();

            // ── Greedy nearest-neighbor, one-to-one matching by XY position ────
            var usedB = new bool[pointsB.Count];
            var matchedA = new bool[pointsA.Count];

            for (int i = 0; i < pointsA.Count; i++)
            {
                int bestJ = -1;
                double bestDist = double.MaxValue;

                for (int j = 0; j < pointsB.Count; j++)
                {
                    if (usedB[j]) continue;

                    double dx = pointsA[i].Position.X - pointsB[j].Position.X;
                    double dy = pointsA[i].Position.Y - pointsB[j].Position.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist <= positionToleranceFt && dist < bestDist)
                    {
                        bestDist = dist;
                        bestJ = j;
                    }
                }

                if (bestJ >= 0)
                {
                    usedB[bestJ] = true;
                    matchedA[i] = true;

                    double elevA = pointsA[i].ElevationMm;
                    double elevB = pointsB[bestJ].ElevationMm;
                    bool sameElevation = Math.Abs(elevB - elevA) <= elevationToleranceMm;

                    entries.Add(new PointComparisonEntry
                    {
                        Status = sameElevation ? PointComparisonStatus.Matched : PointComparisonStatus.MismatchedElevation,
                        Position = pointsA[i].Position,
                        ElevationA_mm = elevA,
                        ElevationB_mm = elevB
                    });
                }
            }

            // ── Unmatched A points — exist in Floor A, missing from Floor B ──────
            for (int i = 0; i < pointsA.Count; i++)
            {
                if (matchedA[i]) continue;
                entries.Add(new PointComparisonEntry
                {
                    Status = PointComparisonStatus.MissingInB,
                    Position = pointsA[i].Position,
                    ElevationA_mm = pointsA[i].ElevationMm
                });
            }

            // ── Unmatched B points — exist in Floor B, missing from Floor A ──────
            for (int j = 0; j < pointsB.Count; j++)
            {
                if (usedB[j]) continue;
                entries.Add(new PointComparisonEntry
                {
                    Status = PointComparisonStatus.MissingInA,
                    Position = pointsB[j].Position,
                    ElevationB_mm = pointsB[j].ElevationMm
                });
            }

            var metrics = BuildMetrics(pointsA, pointsB, entries);

            return new ComparisonResult { Entries = entries, Metrics = metrics };
        }

        private static ComparisonMetrics BuildMetrics(
            List<FloorPointSample> pointsA,
            List<FloorPointSample> pointsB,
            List<PointComparisonEntry> entries)
        {
            var metrics = new ComparisonMetrics
            {
                TotalPointsA = pointsA.Count,
                TotalPointsB = pointsB.Count,
                MatchedCount = entries.Count(e => e.Status == PointComparisonStatus.Matched),
                MismatchedCount = entries.Count(e => e.Status == PointComparisonStatus.MismatchedElevation),
                MissingCount = entries.Count(e => e.Status == PointComparisonStatus.MissingInA
                                                || e.Status == PointComparisonStatus.MissingInB)
            };

            var elevA = pointsA.Select(p => p.ElevationMm).ToList();
            var elevB = pointsB.Select(p => p.ElevationMm).ToList();

            metrics.HighestElevationA_mm = elevA.Count > 0 ? elevA.Max() : 0;
            metrics.LowestElevationA_mm = elevA.Count > 0 ? elevA.Min() : 0;
            metrics.AverageElevationA_mm = elevA.Count > 0 ? elevA.Average() : 0;
            metrics.StdDevElevationA_mm = StdDev(elevA);

            metrics.HighestElevationB_mm = elevB.Count > 0 ? elevB.Max() : 0;
            metrics.LowestElevationB_mm = elevB.Count > 0 ? elevB.Min() : 0;
            metrics.AverageElevationB_mm = elevB.Count > 0 ? elevB.Average() : 0;
            metrics.StdDevElevationB_mm = StdDev(elevB);

            var matchedEntries = entries.Where(e => e.Status == PointComparisonStatus.Matched).ToList();
            metrics.AverageElevationDiffMatched_mm = matchedEntries.Count > 0
                ? matchedEntries.Average(e => e.ElevationDiffMm)
                : 0;

            return metrics;
        }

        /// <summary>Population standard deviation (divides by N, not N-1).</summary>
        private static double StdDev(List<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            double mean = values.Average();
            double sumSq = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumSq / values.Count);
        }
    }
}
