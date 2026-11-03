// =======================================================
// File: PerpendicularPointModel.cs
// Location: Core/Models/
// Changes vs V003: converted from hand-rolled INotifyPropertyChanged to
// CommunityToolkit.Mvvm's ObservableObject + [ObservableProperty], per
// this suite's stack convention.
// =======================================================

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Models
{
    /// <summary>
    /// Represents one side of a selected 4-sided (rectangular) inner loop and the
    /// single perpendicular candidate point chosen on the outer floor border.
    /// </summary>
    public partial class PerpendicularPointModel : ObservableObject
    {
        /// <summary>Index of the owning rectangular loop (matches FloorLoopModel.Index).</summary>
        public int ShapeIndex { get; set; }

        /// <summary>Side label: Top / Right / Bottom / Left (by curve order in the loop).</summary>
        public string Side { get; set; }

        /// <summary>Number of outer-border curve segments evaluated for this side.</summary>
        public int CandidateCount { get; set; }

        /// <summary>The chosen point's XY position on the outer border (closest perpendicular foot).</summary>
        public XYZ BorderPoint { get; set; }

        /// <summary>
        /// Unit vector pointing inward from BorderPoint toward the rectangular loop's side
        /// midpoint. Used to nudge the point off the boundary edge when AddPoint rejects a
        /// zero-offset (exactly-on-border) point. Null/zero-length if it could not be computed.
        /// </summary>
        public XYZ Direction { get; set; }

        /// <summary>Distance in mm from the side to the chosen border point (for log/diagnostics).</summary>
        public double DistanceMm { get; set; }

        /// <summary>Elevation (internal feet) inherited from the lowest adjacent SlabShapeVertex.</summary>
        public double ElevationInternal { get; set; }

        /// <summary>Elevation in mm, for display.</summary>
        public double ElevationMm => UnitUtils.ConvertFromInternalUnits(ElevationInternal, UnitTypeId.Millimeters);

        /// <summary>Display text for the chosen point, e.g. "(12.40, 8.10)".</summary>
        public string PointDisplay => BorderPoint == null
            ? "—"
            : $"({UnitUtils.ConvertFromInternalUnits(BorderPoint.X, UnitTypeId.Meters):0.00}, " +
              $"{UnitUtils.ConvertFromInternalUnits(BorderPoint.Y, UnitTypeId.Meters):0.00})";

        /// <summary>True when no valid perpendicular candidate could be found on the outer border.</summary>
        public bool IsValid => BorderPoint != null;

        /// <summary>Whether this point is included when "Apply Perpendicular Points" runs.</summary>
        [ObservableProperty]
        private bool isSelected = true;
    }
}
