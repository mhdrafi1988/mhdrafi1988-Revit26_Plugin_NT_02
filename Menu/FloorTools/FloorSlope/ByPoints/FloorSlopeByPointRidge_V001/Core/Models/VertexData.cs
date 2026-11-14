// =======================================================
// File: VertexData.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001
// Changes vs V028:
//   Added ridge fields — IsRidgePoint, BasinGroup (nearest drain
//   group), FlowsToGroup (group the final elevation is measured
//   from), NearestPathMeters (V028 path), RidgeLiftMm (how much
//   the ridge rule raised the vertex above its V028 value) and
//   SurroundingGroups (text list of the Voronoi-neighbour groups).
//   PathLengthMeters is now the EFFECTIVE path: elevation ÷ slope,
//   so the slope×path identity still holds for every row.
// =======================================================

using Autodesk.Revit.DB;
using System;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Models
{
    public class VertexData
    {
        public int VertexIndex { get; set; }
        public XYZ Position { get; set; }

        /// <summary>Effective path (m): final elevation ÷ slope factor.</summary>
        public double PathLengthMeters { get; set; }

        /// <summary>Plain shortest path (m) to the nearest drain group — the V028 value.</summary>
        public double NearestPathMeters { get; set; }

        // Elevation calculated by the engine and WRITTEN to the vertex.
        private double _elevationOffsetMm;
        public double ElevationOffsetMm
        {
            get => Math.Round(_elevationOffsetMm, 0);
            set => _elevationOffsetMm = value;
        }

        // Elevation READ BACK from the floor vertex after tx.Commit().
        private double _elevationFromModel_mm;
        public double ElevationFromModel_mm
        {
            get => Math.Round(_elevationFromModel_mm, 0);
            set => _elevationFromModel_mm = value;
        }

        // Difference: model value minus calculated value.
        public double ElevationDiff_mm => ElevationFromModel_mm - ElevationOffsetMm;

        public int NearestDrainIndex { get; set; }
        public XYZ DirectionVector { get; set; }
        public bool WasProcessed { get; set; }

        // ── Ridge (V001) ─────────────────────────────────────────────────
        /// <summary>True when the ridge rule applied to this vertex (Voronoi boundary point).</summary>
        public bool IsRidgePoint { get; set; }

        /// <summary>Nearest drain group (path-distance Voronoi cell). -1 = unreachable.</summary>
        public int BasinGroup { get; set; } = -1;

        /// <summary>Group the final elevation is measured from (after "move watershed"). -1 = unreachable.</summary>
        public int FlowsToGroup { get; set; } = -1;

        /// <summary>Millimetres the vertex was raised above its plain V028 value (0 for untouched vertices).</summary>
        public double RidgeLiftMm { get; set; }

        /// <summary>Comma-separated groups whose basins meet at this ridge point (empty for non-ridge).</summary>
        public string SurroundingGroups { get; set; } = string.Empty;

        public string Direction =>
            DirectionVector != null ?
            $"{DirectionVector.X:F3},{DirectionVector.Y:F3},{DirectionVector.Z:F3}" :
            "0,0,0";
    }
}
