// =======================================================
// File: VertexData.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models
// Changes vs V030:
//   - Point3 instead of XYZ (Core stays Revit-free).
//   - Raw values are stored; rounding happens only for display/Excel
//     (V030 rounded inside the getters).
//   - DrainIndex is the drain the shortest PATH reaches, recorded by the
//     solver, not the straight-line nearest drain (V032 D4).
//   - Outcome says why a point was skipped (beyond distance / no path).
// =======================================================

using System;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models
{
    public sealed class VertexData
    {
        public int VertexIndex { get; set; }
        public Point3 Position { get; set; }
        public bool IsDrain { get; set; }
        public VertexOutcome Outcome { get; set; }
        public bool WasProcessed => Outcome == VertexOutcome.Processed;

        /// <summary>Shortest path length to its drain, metres. 0 when there is no path.</summary>
        public double PathLengthMeters { get; set; }

        /// <summary>Elevation the run wrote: path length × slope, millimetres.</summary>
        public double ElevationCalcMm { get; set; }

        /// <summary>Elevation read back from Revit after commit, relative to the drains, millimetres.</summary>
        public double ElevationModelMm { get; set; }

        public bool HasModelValue { get; set; }

        /// <summary>Ordinal of the drain this point drains to (0-based), or -1.</summary>
        public int DrainIndex { get; set; } = -1;

        /// <summary>Next vertex along the path towards the drain, or -1.</summary>
        public int NextHopVertex { get; set; } = -1;

        /// <summary>Model minus calculated, both rounded to whole mm.</summary>
        public double ElevationDiffMm =>
            HasModelValue ? Math.Round(ElevationModelMm) - Math.Round(ElevationCalcMm) : 0;
    }
}
