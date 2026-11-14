// ==================================
// File: MinimumLengthFilterService.cs
// Namespace: Revit26_Plugin_Revised.FloorCreaser.V001
// ==================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorCreaser.V001.Services
{
    /// <summary>
    /// Filters crease lines by minimum length threshold.
    /// Lines shorter than the specified minimum are removed.
    /// </summary>
    public class MinimumLengthFilterService
    {
        private readonly LoggingService _log;

        public MinimumLengthFilterService(LoggingService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Filters crease curves + projected lines together, in lockstep, so the
        /// 3D-curve/2D-line pairing stays intact for downstream steps (e.g.
        /// DrainPointGroupingService, which requires matching counts).
        /// </summary>
        /// <param name="curves3d">Original 3D crease curves — same order/count as lines2d.</param>
        /// <param name="lines2d">Projected 2D crease lines.</param>
        /// <param name="minLengthMm">Minimum length threshold in millimeters.</param>
        /// <returns>Filtered (curves3d, lines2d) pair, index-aligned.</returns>
        public (IList<Curve> Curves3d, IList<Line> Lines2d) FilterByMinimumLength(
            IList<Curve> curves3d,
            IList<Line>  lines2d,
            double       minLengthMm)
        {
            if (curves3d.Count != lines2d.Count)
                throw new ArgumentException("Curve and line counts must match.");

            if (lines2d.Count == 0)
            {
                _log.Info("No lines to filter by minimum length.");
                return (new List<Curve>(), new List<Line>());
            }

            // Convert mm to Revit internal units (feet)
            // 1 foot = 304.8 mm
            double minLengthFt = minLengthMm / 304.8;

            var keptCurves = new List<Curve>();
            var keptLines  = new List<Line>();
            int skipped = 0;

            for (int i = 0; i < lines2d.Count; i++)
            {
                Line line = lines2d[i];
                if (line == null)
                    continue;

                double len = line.Length;

                if (len >= minLengthFt)
                {
                    keptCurves.Add(curves3d[i]);
                    keptLines.Add(line);
                }
                else
                {
                    skipped++;
                    _log.Debug($"  Crease #{i + 1} dropped (too short): {len * 304.8:F0} mm < {minLengthMm:F0} mm");
                }
            }

            _log.Info($"Minimum length filter (< {minLengthMm:F0} mm): kept {keptLines.Count}, dropped {skipped} of {lines2d.Count}");
            return (keptCurves, keptLines);
        }
    }
}
