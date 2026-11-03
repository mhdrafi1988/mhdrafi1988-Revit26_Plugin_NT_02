// =======================================================
// File: VertexData.cs
// Changes:
//   Added ElevationFromModel_mm — the elevation re-read
//   from the floor vertex AFTER the slope transaction
//   commits. Compared against ElevationOffsetMm (which
//   is calculated from path × slope) to detect any silent
//   adjustments Revit made during commit.
// =======================================================

using Autodesk.Revit.DB;
using System;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models
{
    public class VertexData
    {
        public int VertexIndex { get; set; }
        public XYZ Position { get; set; }
        public double PathLengthMeters { get; set; }

        // Elevation calculated from: PathLength × SlopePercent
        // This is what the engine WROTE to the vertex.
        private double _elevationOffsetMm;
        public double ElevationOffsetMm
        {
            get => Math.Round(_elevationOffsetMm, 0);
            set => _elevationOffsetMm = value;
        }

        // Elevation READ BACK from the floor vertex after tx.Commit().
        // Reflects what Revit actually stored — may differ from
        // ElevationOffsetMm if Revit clamped or adjusted the value.
        private double _elevationFromModel_mm;
        public double ElevationFromModel_mm
        {
            get => Math.Round(_elevationFromModel_mm, 0);
            set => _elevationFromModel_mm = value;
        }

        // Difference: model value minus calculated value.
        // Zero = Revit accepted exactly what was written.
        // Non-zero = Revit silently adjusted the vertex.
        public double ElevationDiff_mm => ElevationFromModel_mm - ElevationOffsetMm;

        public int NearestDrainIndex { get; set; }
        public XYZ DirectionVector { get; set; }
        public bool WasProcessed { get; set; }

        public string Direction =>
            DirectionVector != null ?
            $"{DirectionVector.X:F3},{DirectionVector.Y:F3},{DirectionVector.Z:F3}" :
            "0,0,0";
    }
}
