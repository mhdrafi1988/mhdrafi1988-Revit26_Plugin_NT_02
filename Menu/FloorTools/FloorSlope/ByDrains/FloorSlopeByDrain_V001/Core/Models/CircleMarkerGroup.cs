// File: CircleMarkerGroup.cs
// Location: Core/Models/
// Ported from AutoSlopeByPoint V028's CircleMarkerGroup.cs (see that file's
// header for the original design notes) — same style/config unit for a
// circle-marker group (Drain / Highest Point), adapted to this namespace.
//
// Notes:
//   - LineStyleId/LineStyleName describe an existing project GraphicsStyle
//     under OST_Lines (Line Style), picked by the user. Never creates new
//     line styles.
//   - ColorName is a named WPF color applied as a per-view DetailCurve
//     graphic override — independent of the chosen Line Style's own color.
//   - RadiusMm is entered in millimeters and converted to internal (feet)
//     units at the point of use in CircleMarkerService.

using CommunityToolkit.Mvvm.ComponentModel;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models
{
    public partial class CircleMarkerGroup : ObservableObject
    {
        /// <summary>Display label for this group, e.g. "Drain", "Highest Point". UI-only, not persisted.</summary>
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
        /// Ignored by the Drain group.
        /// </summary>
        [ObservableProperty]
        private bool showOffsetText = true;

        /// <summary>
        /// NEW (V013). Copy taken when Run starts, so changes made in the window
        /// while a run is in progress cannot reach the run (V012 passed the live
        /// objects).
        /// </summary>
        public CircleMarkerGroup Snapshot() => new CircleMarkerGroup
        {
            GroupLabel = GroupLabel,
            IsEnabled = IsEnabled,
            LineStyleId = LineStyleId,
            LineStyleName = LineStyleName,
            ColorName = ColorName,
            RadiusMm = RadiusMm,
            ShowOffsetText = ShowOffsetText
        };
    }
}
