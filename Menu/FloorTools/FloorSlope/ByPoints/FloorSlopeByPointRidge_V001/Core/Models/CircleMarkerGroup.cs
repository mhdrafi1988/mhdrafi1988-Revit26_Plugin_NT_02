// =======================================================
// File: CircleMarkerGroup.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001
// New in V026.
// Purpose: Single reusable style/config unit for a circle-marker
//          group (Drains / Highest Point / Allowed Offset).
//          Instantiated 3x on the ViewModel — one per group —
//          instead of hand-writing 3 parallel sets of properties.
// Notes:
//   - LineStyleId/LineStyleName describe an existing project
//     GraphicsStyle under OST_Lines (Line Style), picked by the
//     user. The tool never creates new line styles (per V026 spec).
//   - ColorName is a named WPF color (System.Windows.Media.Colors)
//     applied as a per-view DetailCurve graphic override — it is
//     independent of whatever color the chosen Line Style itself
//     carries.
//   - RadiusMm is entered in millimeters and converted to internal
//     (feet) units at the point of use in CircleMarkerService.
// =======================================================

using CommunityToolkit.Mvvm.ComponentModel;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Models
{
    public partial class CircleMarkerGroup : ObservableObject
    {
        /// <summary>Display label for this group, e.g. "Drain", "Highest Point", "Allowed Offset". UI-only, not persisted.</summary>
        public string GroupLabel { get; set; }

        [ObservableProperty]
        private bool isEnabled = true;

        /// <summary>ElementId of the chosen Line Style (GraphicsStyle under OST_Lines subcategories).</summary>
        [ObservableProperty]
        private ElementId lineStyleId;

        /// <summary>Display name of the chosen Line Style — kept alongside the Id for settings persistence and log messages.</summary>
        [ObservableProperty]
        private string lineStyleName;

        /// <summary>Named WPF color (e.g. "Blue", "Red") applied as a per-view graphic override on placed circles.</summary>
        [ObservableProperty]
        private string colorName = "Black";

        /// <summary>Circle radius in millimeters, as entered by the user.</summary>
        [ObservableProperty]
        private double radiusMm = 250;

        /// <summary>
        /// Only meaningful for the Highest Point group: whether to also place
        /// the red "Offset: X mm" text label (with leader) next to the circle.
        /// Ignored by the Drain and Allowed Offset groups.
        /// </summary>
        [ObservableProperty]
        private bool showOffsetText = true;
    }
}
