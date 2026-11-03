// =======================================================
// File: SlopeRowSetting.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// Purpose: One of the 4 fixed slope rows in the Multi-Slope
//          Floor Variants UI — a checkbox (IsEnabled) plus a
//          typable/searchable slope percentage.
// =======================================================

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models
{
    public class SlopeRowSetting
    {
        /// <summary>1-based row index (1-4), used for logging and default ordering.</summary>
        public int RowIndex { get; set; }

        /// <summary>Row is active — a floor copy is generated for this slope when true.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>Slope percentage applied to this row's copy.</summary>
        public double Percent { get; set; }
    }
}
