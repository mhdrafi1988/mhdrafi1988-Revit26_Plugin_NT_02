using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models
{
    /// <summary>
    /// How the usable placement area was determined for the active sheet.
    ///
    /// V008 CHANGE: the V006/V007 RightEdge/BottomEdge/Corner classification
    /// model is REMOVED. Root cause: it compared the title block's bbox
    /// against a separately-derived "sheet bbox" (from
    /// ViewSheet.get_BoundingBox(null)), which Revit's API does not return
    /// reliably for sheets — it frequently yields a fixed ±100ft generic
    /// placeholder instead of the sheet's real extent, so every "hugs edge"
    /// comparison failed and every sheet fell through to Undetected.
    ///
    /// The corrected model (confirmed working in this suite's own
    /// SmartViewToSheetPlacer tool): the title block FamilyInstance's own
    /// bounding box IS the reliable, authoritative reference — there is no
    /// separate "sheet bbox" to compare it against. The placeable area is
    /// simply the title block's bbox, inset by the user's configured
    /// margins (GapSettings.MarginTopMm/BottomMm/LeftMm/RightMm).
    /// </summary>
    public enum TitleBlockDetectionMode
    {
        /// <summary>Title block instance found; usable area = its bbox inset by margins.</summary>
        Detected,

        /// <summary>No title block instance found (or 2+ found — see TitleBlockDetectionResult.MultipleTitleBlocksFound), or its bbox could not be read. Caller must fall back to manual input.</summary>
        Undetected,

        /// <summary>User-entered rectangle — used after Undetected, or if the user overrides a successful detection.</summary>
        Manual
    }

    /// <summary>
    /// A single axis-aligned rectangle in sheet space (feet, Revit internal units).
    /// </summary>
    public readonly struct RectFeet
    {
        public XYZ Min { get; }
        public XYZ Max { get; }

        public RectFeet(XYZ min, XYZ max)
        {
            Min = min;
            Max = max;
        }

        public double Width => Max.X - Min.X;
        public double Height => Max.Y - Min.Y;
    }

    /// <summary>
    /// Resolved usable placement area for a sheet. LargeRect is the title
    /// block's bbox inset by margins.
    ///
    /// V021 CLEANUP: SmallRect and IsLShape removed. They were vestigial
    /// since V008 (title block classification never produced a second
    /// rectangle) and fully dead since V014 (the Tall/Wide anchor system
    /// that could have carved out a second region — via an oversized VIEW,
    /// unrelated to title block shape — was removed entirely, replaced by
    /// MasterRowBandPackingService). All 3 constructor call sites always
    /// passed null. GetUsableXRangeAtY/GetOverallBounds simplified
    /// accordingly — they now always resolve to LargeRect.
    /// </summary>
    public class PlaceableRegion
    {
        public TitleBlockDetectionMode Mode { get; }

        /// <summary>The usable rectangle — the full placeable area (title block bbox inset by margins).</summary>
        public RectFeet LargeRect { get; }

        /// <summary>Human-readable classification text for the UI (e.g. "Detected", "Manual").</summary>
        public string DisplayText { get; }

        public PlaceableRegion(TitleBlockDetectionMode mode, RectFeet largeRect, string displayText)
        {
            Mode = mode;
            LargeRect = largeRect;
            DisplayText = displayText;
        }

        public (double minX, double maxX, RectFeet usedRect) GetUsableXRangeAtY(double bandTop, double bandBottom)
            => (LargeRect.Min.X, LargeRect.Max.X, LargeRect);

        public RectFeet GetOverallBounds() => LargeRect;
    }
}
