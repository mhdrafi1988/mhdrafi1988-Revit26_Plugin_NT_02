// =======================================================
// File: FloorLoopModel.cs
// Location: Core/Models/
// Changes vs V011:
//   + Loop (plan geometry), Shape, SizeText, Source, ExistingVertices.
//   + Live plan: PlannedTotal / PlannedNew / PlannedExisting, updated by
//     UpdatePlan() whenever the count, mode or spacing changes.
//   ~ RecommendedPoints now means "points around the WHOLE loop" (V011
//     placed that many on every curve of the loop).
//   ~ DividedLengthMeters = perimeter ÷ planned points (now matches).
//   = Index, PerimeterMm, LoopType, IsCircular, LoopShapeType,
//     ShapeCategory, CategoryRank, IsSelected, Geometry kept with the same
//     names so CombinedRoofTools can bind to V012 unchanged.
// =======================================================

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Models
{
    public partial class FloorLoopModel : ObservableObject
    {
        /// <summary>Opening number, stable: north to south, then west to east.</summary>
        public int Index { get; set; }

        /// <summary>Perimeter of the loop in millimetres.</summary>
        public double PerimeterMm { get; set; }

        /// <summary>Boundary role — always <c>Inner</c> for openings.</summary>
        public string LoopType { get; set; }

        public bool IsCircular { get; set; }

        /// <summary>V011-compatible classification: Circular / Rectangle / Other.</summary>
        public string LoopShapeType { get; set; }

        /// <summary>Where the opening was found: "Sketch" or "Opening cut".</summary>
        public string Source { get; set; }

        /// <summary>"Ø 150" or "300 × 200" (mm).</summary>
        public string SizeText { get; set; }

        /// <summary>Plan geometry used for planning and writing.</summary>
        public Loop2D Loop { get; set; }

        public LoopShapeInfo Shape { get; set; }

        /// <summary>The floor's shape points (plan XY, feet) — shared by all loops of one floor.</summary>
        public IReadOnlyList<P2> ExistingVertices { get; set; }

        /// <summary>Top-face edge loop when one was found (kept for V011 compatibility; not used to place points).</summary>
        public CurveLoop Geometry { get; set; }

        public string ShapeCategory => LoopShapeType switch
        {
            "Circular"  => "Circular",
            "Rectangle" => "Rectangular",
            _           => "Other"
        };

        public int CategoryRank => LoopShapeType switch
        {
            "Circular"  => 0,
            "Rectangle" => 1,
            _           => 2
        };

        [ObservableProperty]
        private bool isSelected = true;

        /// <summary>Points around the whole loop (Count mode). Corners are part of this number.</summary>
        [ObservableProperty]
        private int recommendedPoints = 6;

        /// <summary>False in Spacing mode — the count is then worked out from the spacing.</summary>
        [ObservableProperty]
        private bool isCountMode = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DividedLengthMeters))]
        private int plannedTotal;

        [ObservableProperty]
        private int plannedNew;

        [ObservableProperty]
        private int plannedExisting;

        private double _averageSpacingMm;

        /// <summary>Gap between points along the loop in metres ("—" when none).</summary>
        public string DividedLengthMeters =>
            PlannedTotal <= 0 ? "—" : (_averageSpacingMm / 1000.0).ToString("0.00");

        /// <summary>Recomputes the planned points for the current settings (pure geometry, no Revit calls).</summary>
        public void UpdatePlan(DivisionMode mode, double spacingFeet, double toleranceFeet)
        {
            IsCountMode = mode == DivisionMode.Count;
            DivisionPlan plan = Loop == null
                ? DivisionPlan.Empty
                : DivisionPlanner.Plan(Loop, mode, RecommendedPoints, spacingFeet, ExistingVertices, toleranceFeet);

            _averageSpacingMm = plan.AverageSpacing * 304.8;
            PlannedNew = plan.NewPoints;
            PlannedExisting = plan.ExistingPoints;
            PlannedTotal = plan.TotalPoints;
            OnPropertyChanged(nameof(DividedLengthMeters));
        }
    }
}
