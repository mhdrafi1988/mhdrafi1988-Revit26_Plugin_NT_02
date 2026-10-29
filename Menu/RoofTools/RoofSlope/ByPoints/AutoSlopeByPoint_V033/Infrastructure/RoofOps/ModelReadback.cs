// =======================================================
// File: ModelReadback.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// Post-commit re-read, split out of V030's AutoSlopeEngine. Changes:
//   - Spatial-grid lookup instead of the O(n²) double loop (P2).
//   - Re-fetches the SlabShapeEditor after the commit (V030 reused the
//     pre-commit handle).
//   - Counts points Revit adjusted and the largest change, so the run can
//     warn when the Highest Point marker (calculated) and the Highest
//     Elevation tile (read back) may differ (D7).
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    internal sealed class ReadbackOutcome
    {
        public double HighestElevationMm { get; set; }
        public int Unmatched { get; set; }
        public int AdjustedCount { get; set; }
        public double MaxAdjustmentMm { get; set; }
    }

    internal static class ModelReadback
    {
        public static ReadbackOutcome Read(RoofBase roof, IReadOnlyList<VertexData> rows, IReadOnlyList<int> drainVertices)
        {
            var outcome = new ReadbackOutcome();

            SlabShapeEditor editor = RoofPreparer.GetEditor(roof);
            var refreshed = RoofPreparer.Collect(editor).Select(v => v.Position.ToPoint3()).ToList();
            var grid = new SpatialGrid2D(refreshed, 0.05);
            double tol = AutoSlopeConstants.ReadbackMatchFt;

            double? Lookup(Point3 p)
            {
                var hits = grid.QueryXY(p, tol);
                if (hits.Count == 0) return null;
                int best = hits.OrderBy(h => refreshed[h].DistanceXY(p)).First();
                return refreshed[best].Z;
            }

            // Baseline: drains stay at offset 0, so their read-back height is the datum.
            var drainZs = drainVertices
                .Where(i => i >= 0 && i < rows.Count)
                .Select(i => Lookup(rows[i].Position))
                .Where(z => z.HasValue)
                .Select(z => z.Value)
                .ToList();
            double baselineFt = drainZs.Count > 0 ? drainZs.Average() : rows.Where(r => r.IsDrain).Select(r => r.Position.Z).DefaultIfEmpty(0).Average();

            double maxFt = 0;
            foreach (VertexData row in rows)
            {
                if (!row.WasProcessed) continue;

                double? z = Lookup(row.Position);
                if (!z.HasValue)
                {
                    row.ElevationModelMm = row.ElevationCalcMm;
                    row.HasModelValue = false;
                    outcome.Unmatched++;
                    continue;
                }

                double elevFt = z.Value - baselineFt;
                row.ElevationModelMm = RevitConvert.FtToMm(elevFt);
                row.HasModelValue = true;
                if (elevFt > maxFt) maxFt = elevFt;

                double diff = Math.Abs(row.ElevationDiffMm);
                if (diff >= AutoSlopeConstants.AdjustmentWarningMm)
                {
                    outcome.AdjustedCount++;
                    if (diff > outcome.MaxAdjustmentMm) outcome.MaxAdjustmentMm = diff;
                }
            }

            outcome.HighestElevationMm = Math.Round(RevitConvert.FtToMm(maxFt), MidpointRounding.AwayFromZero);
            return outcome;
        }
    }
}
