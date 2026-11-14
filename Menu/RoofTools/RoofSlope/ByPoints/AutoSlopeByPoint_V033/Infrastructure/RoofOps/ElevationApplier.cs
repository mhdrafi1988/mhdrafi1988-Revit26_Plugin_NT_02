// =======================================================
// File: ElevationApplier.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// The "Apply AutoSlope" loop, split out of V030's AutoSlopeEngine.
// Must run inside an open Transaction (the runner's "Apply AutoSlope"
// transaction, which also holds the circle markers: one commit, one undo
// step — the confirmed V026 design is kept).
// Changes vs V030:
//   - Skipped points record why (beyond distance / no path) — D5.
//   - DrainIndex / NextHop come from the solver's path, not the
//     straight-line nearest drain — D4.
//   - Cancellation is checked inside the loop; safe now that the handler
//     rolls the whole group back on cancel (B1).
// =======================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    internal sealed class ApplyOutcome
    {
        public List<VertexData> Vertices { get; } = new List<VertexData>();
        public int Processed { get; set; }
        public int BeyondDistance { get; set; }
        public int NoPath { get; set; }
        public double LongestPathFt { get; set; }
    }

    internal static class ElevationApplier
    {
        public static ApplyOutcome Apply(
            SlabShapeEditor editor,
            IReadOnlyList<SlabShapeVertex> vertices,
            IReadOnlyList<XYZ> positions,
            ShortestPathResult paths,
            IReadOnlyList<int> drainVertices,
            AutoSlopeRequest request,
            Action<double> onProgress,
            CancellationToken cancel)
        {
            var outcome = new ApplyOutcome();
            double slopeFactor = request.SlopePercent / 100.0;
            double maxDistFt = RevitConvert.MToFt(request.MaxDrainDistanceM);

            var drainOrdinal = new Dictionary<int, int>();
            for (int k = 0; k < drainVertices.Count; k++)
                drainOrdinal[drainVertices[k]] = k;

            int n = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                if ((i & 0xFF) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    onProgress?.Invoke(n <= 1 ? 100 : i * 100.0 / (n - 1));
                }

                double pathFt = paths.Distance[i];
                var vd = new VertexData
                {
                    VertexIndex = i,
                    Position = positions[i].ToPoint3(),
                    IsDrain = drainOrdinal.ContainsKey(i)
                };

                if (double.IsInfinity(pathFt))
                {
                    vd.Outcome = VertexOutcome.NoPath;
                    outcome.NoPath++;
                    outcome.Vertices.Add(vd);
                    continue;
                }

                vd.PathLengthMeters = RevitConvert.FtToM(pathFt);
                if (pathFt > maxDistFt)
                {
                    vd.Outcome = VertexOutcome.BeyondDistance;
                    outcome.BeyondDistance++;
                    outcome.Vertices.Add(vd);
                    continue;
                }

                double elevFt = pathFt * slopeFactor;
                editor.ModifySubElement(vertices[i], elevFt);

                vd.Outcome = VertexOutcome.Processed;
                vd.ElevationCalcMm = RevitConvert.FtToMm(elevFt);
                int src = paths.SourceDrain[i];
                vd.DrainIndex = src >= 0 && drainOrdinal.TryGetValue(src, out int ord) ? ord : -1;
                vd.NextHopVertex = paths.NextHop[i];

                outcome.Processed++;
                if (pathFt > outcome.LongestPathFt) outcome.LongestPathFt = pathFt;
                outcome.Vertices.Add(vd);
            }

            onProgress?.Invoke(100);
            return outcome;
        }
    }
}
