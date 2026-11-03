using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models
{
    /// <summary>
    /// Style/config for one marker type (Missing Points / Mismatched Elevation).
    /// ColorName is a named WPF color applied as a per-view DetailCurve graphic
    /// override; RadiusMm is entered in millimeters and converted to internal
    /// (feet) units at the point of use in ComparisonMarkerService.
    /// </summary>
    public partial class MarkerStyleGroup : ObservableObject
    {
        public string GroupLabel { get; set; }

        [ObservableProperty]
        private string colorName = "Black";

        [ObservableProperty]
        private double radiusMm = 150;
    }
}
