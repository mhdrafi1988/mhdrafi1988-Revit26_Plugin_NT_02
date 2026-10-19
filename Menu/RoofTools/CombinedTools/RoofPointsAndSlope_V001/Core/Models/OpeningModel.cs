// =======================================================
// File: OpeningModel.cs
// Location: Core/Models/
// One roof opening, as a row in the window's grid. Each opening carries
// its own choices for the opening steps: points around it,
// perpendicular points to the boundary, and use as a drain (a drain
// seeds the ridge and is a low point of the slope). The planned
// counts are filled in by the view model after each re-plan.
// =======================================================

using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>One roof opening and the user's choices for it.</summary>
    public partial class OpeningModel : ObservableObject
    {
        /// <summary>Opening number, stable: north to south, then west to east.</summary>
        public int Index { get; init; }

        /// <summary>Plan geometry.</summary>
        public Loop2D Loop { get; init; }

        /// <summary>Shape and size.</summary>
        public LoopShapeInfo Shape { get; init; }

        /// <summary>"Circular", "Rectangle" or "Other".</summary>
        public string ShapeName => Shape?.Name ?? "Other";

        /// <summary>"Ø 150" or "300 × 200" (mm).</summary>
        public string SizeText { get; init; }

        /// <summary>Where the opening was found: "Sketch" or "Opening cut".</summary>
        public string Source { get; init; }

        /// <summary>
        /// The matching opening from Auto Slope By Drain V013's detection, or null when it found
        /// none here. Only openings with a match can be used as drains.
        /// </summary>
        public DrainItem Drain { get; set; }

        /// <summary>True when the opening can be used as a drain.</summary>
        public bool CanBeDrain => Drain != null;

        /// <summary>Step 1: add points around this opening.</summary>
        [ObservableProperty] private bool addInnerPoints = true;

        /// <summary>Step 1, count mode: points around the whole opening, corners included.</summary>
        [ObservableProperty] private int pointCount = 6;

        /// <summary>Step 2: add perpendicular points from this opening to the boundary.</summary>
        [ObservableProperty] private bool addPerpendicular = true;

        /// <summary>Steps 5–6: a ridge seed, and a low point the roof slopes towards.</summary>
        [ObservableProperty] private bool isDrain;

        /// <summary>Planned new points around this opening (step 1).</summary>
        [ObservableProperty] private int plannedInner;

        /// <summary>Planned new perpendicular points from this opening (step 2).</summary>
        [ObservableProperty] private int plannedPerpendicular;
    }
}
