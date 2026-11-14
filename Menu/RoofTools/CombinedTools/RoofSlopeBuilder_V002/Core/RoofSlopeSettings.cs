// =======================================================
// File: RoofSlopeSettings.cs
// Namespace: Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core
// All lengths are in Revit internal units (feet), areas in square feet.
// The dialog converts from mm / m2 before filling this in.
// Saved as JSON (properties only) under
// %AppData%\Revit26_Plugin_Revised\RoofSlopeBuilder\settings_v002.json
// (version 2; a V001 settings.json is read once and migrated).
// =======================================================
namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core
{
    /// <summary>What to do with a curve too short for the minimum gap between its points.</summary>
    public enum GapMode { ReduceCount, KeepCount, SkipCurve }

    /// <summary>How the ridge between two drains is built.</summary>
    public enum RidgeMode { Watershed, Saddle }

    /// <summary>V002: what the ridge lines are drawn with.</summary>
    public enum RidgeLineKind { DetailLine, DetailFamily }

    /// <summary>
    /// Every option of one Roof Slope Builder run. Lengths in feet, areas in square feet.
    /// </summary>
    public class RoofSlopeSettings
    {
        /// <summary>Format version of the saved settings file. Raise it when a field changes meaning.</summary>
        public const int CurrentVersion = 2;

        /// <summary>Format version this instance was saved with (0 = file written before versioning).</summary>
        public int SettingsVersion { get; set; } = CurrentVersion;

        // ---- points ----
        /// <summary>Add the corners of openings that are not drains.</summary>
        public bool IncludeInnerLoops { get; set; } = true;
        /// <summary>Openings smaller than this get no corner points.</summary>
        public double InnerMinArea { get; set; } = 0.05 * 10.7639104;      // 0.05 m2

        /// <summary>Add perpendicular feet from each drain.</summary>
        public bool AddPerpendicular { get; set; } = true;
        /// <summary>Perpendicular feet onto straight boundary edges.</summary>
        public bool PerpToBoundary { get; set; } = true;
        /// <summary>Perpendicular feet onto the ridges around the drain (watershed mode).</summary>
        public bool PerpToRidge { get; set; } = true;

        // ---- curves ----
        /// <summary>Add points along arcs and splines.</summary>
        public bool AddCurvePoints { get; set; } = true;
        /// <summary>Points on a curve longer than <see cref="LargeCurveLength"/>.</summary>
        public int CurvePointCount { get; set; } = 4;
        /// <summary>A curve longer than this counts as large.</summary>
        public double LargeCurveLength { get; set; } = 2000 / 304.8;
        /// <summary>Small curves get one point at their middle.</summary>
        public bool SmallCurveMidpoint { get; set; } = true;
        /// <summary>Minimum gap between points on one curve.</summary>
        public double MinGap { get; set; } = 300 / 304.8;
        /// <summary>What to do when the gap cannot be met.</summary>
        public GapMode GapMode { get; set; } = GapMode.ReduceCount;

        // ---- drains ----
        /// <summary>Openings larger than this are not drains (hatches, skylights).</summary>
        public double DrainMaxArea { get; set; } = 0.25 * 10.7639104;      // 0.25 m2
        /// <summary>Select only the smallest opening size by default.</summary>
        public bool LowestSizeOnly { get; set; } = true;

        // ---- slope ----
        /// <summary>Fall as a fraction (0.015 = 1.5 %).</summary>
        public double Slope { get; set; } = 0.015;
        /// <summary>Warn when any fall is flatter than 1 : this.</summary>
        public double MinFallN { get; set; } = 80;
        /// <summary>Warn when a point is higher than this.</summary>
        public double MaxHeightWarn { get; set; } = 300 / 304.8;
        /// <summary>Unit shown in the slope box: 0 = %, 1 = 1 : n, 2 = mm/m.</summary>
        public int SlopeUnitIndex { get; set; }

        // ---- ridges between drains ----
        /// <summary>Add ridge points between drains (roofs with 2 or more drains).</summary>
        public bool AddDrainRidges { get; set; } = true;
        /// <summary>Watershed ridge or saddle (diamond cricket).</summary>
        public RidgeMode RidgeMode { get; set; } = RidgeMode.Watershed;
        /// <summary>Distance between points along a ridge.</summary>
        public double RidgeSpacing { get; set; } = 1500 / 304.8;
        /// <summary>Add split lines along the ridges.</summary>
        public bool AddSplitLines { get; set; } = true;
        /// <summary>Saddle mode: drains farther apart get no saddle.</summary>
        public double MaxDrainSpacing { get; set; } = 24000 / 304.8;
        /// <summary>Saddle width as a part of the drain spacing.</summary>
        public double SaddleWidthRatio { get; set; } = 1.0 / 3.0;
        /// <summary>Saddle slope as a multiple of the field slope.</summary>
        public double SaddleSlopeFactor { get; set; } = 2.0;

        // ---- ridge drawing (V002, settings version 2) ----
        /// <summary>Draw the ridge lines in the active plan view.</summary>
        public bool DrawRidgeLines { get; set; }
        /// <summary>Detail line or line-based detail family.</summary>
        public RidgeLineKind RidgeLineKind { get; set; } = RidgeLineKind.DetailLine;
        /// <summary>Line style for detail lines (and for the fallback). Stored by name: ids differ between models.</summary>
        public string RidgeLineStyle { get; set; } = "Thin Lines";
        /// <summary>Line-based detail family type, as "Family : Type".</summary>
        public string RidgeFamilyType { get; set; } = "";
        /// <summary>Mark every ridge point with a small detail circle.</summary>
        public bool MarkRidgePoints { get; set; }
        /// <summary>Diameter of the ridge point circles.</summary>
        public double RidgePointDiameter { get; set; } = 150 / 304.8;

        // ---- general ----
        /// <summary>Never place two points closer than this.</summary>
        public double MergeTolerance { get; set; } = 100 / 304.8;
        /// <summary>Remove existing shape-edit points first.</summary>
        public bool ResetShape { get; set; } = true;
        /// <summary>Round point heights to this step (0 = no rounding).</summary>
        public double RoundHeightTo { get; set; } = 1 / 304.8;
    }
}
