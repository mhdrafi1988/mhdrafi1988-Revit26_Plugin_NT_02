using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Services
{
    public class TitleBlockDetectionResult
    {
        public PlaceableRegion? Region { get; set; }
        public bool MultipleTitleBlocksFound { get; set; }
        public bool NoTitleBlockFound { get; set; }
    }

    /// <summary>
    /// Locates the title block instance on a sheet and builds a PlaceableRegion
    /// from its bounding box, inset by the configured margins.
    ///
    /// V008 REWRITE: replaces the V006/V007 approach entirely. That version
    /// compared the title block's bbox against ViewSheet.get_BoundingBox(null)
    /// to classify RightEdge/BottomEdge/Corner and subtract a strip — but
    /// Revit's get_BoundingBox(null) does not reliably return a sheet's real
    /// extent (confirmed via a live bug report: it returned a fixed ±100ft
    /// placeholder), so that comparison always failed and every sheet fell
    /// through to Undetected regardless of the actual title block position.
    ///
    /// CORRECTED MODEL (confirmed working in this suite's own
    /// SmartViewToSheetPlacer tool, via FilteredElementCollector + title
    /// block get_BoundingBox(sheet)): the title block instance's own bbox
    /// IS the sheet's real printable frame — there is nothing separate to
    /// subtract it from. The usable/placeable area is simply that bbox,
    /// inset by the user's configured margins. No more RightEdge/BottomEdge/
    /// Corner classification, no more L-shape.
    ///
    /// Multiple title blocks on one sheet → caller skips the sheet entirely
    /// (confirmed: "if two title block present just do nothing").
    /// </summary>
    public class TitleBlockDetectionService
    {
        private const double MmToFeet = 1.0 / 304.8;

        public TitleBlockDetectionResult Detect(Document doc, ViewSheet sheet, GapSettings gapSettings)
        {
            var result = new TitleBlockDetectionResult();

            var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            if (titleBlocks.Count == 0)
            {
                result.NoTitleBlockFound = true;
                return result;
            }

            if (titleBlocks.Count > 1)
            {
                result.MultipleTitleBlocksFound = true;
                return result;
            }

            var tb = titleBlocks[0];
            var tbBox = tb.get_BoundingBox(sheet);

            if (tbBox == null)
            {
                // Can't read geometry — treat as undetected so the caller
                // offers manual input rather than guessing.
                result.Region = null;
                return result;
            }

            double marginTopFeet = gapSettings.MarginTopMm * MmToFeet;
            double marginBottomFeet = gapSettings.MarginBottomMm * MmToFeet;
            double marginLeftFeet = gapSettings.MarginLeftMm * MmToFeet;
            double marginRightFeet = gapSettings.MarginRightMm * MmToFeet;

            var insetMin = new XYZ(tbBox.Min.X + marginLeftFeet, tbBox.Min.Y + marginBottomFeet, 0);
            var insetMax = new XYZ(tbBox.Max.X - marginRightFeet, tbBox.Max.Y - marginTopFeet, 0);

            // Guard against margins collapsing or inverting the rect (e.g.
            // absurdly large margin values entered by the user) — fall back
            // to the raw title block bbox with a note in DisplayText rather
            // than silently producing a negative-size rect that would break
            // every downstream packing calculation.
            if (insetMax.X <= insetMin.X || insetMax.Y <= insetMin.Y)
            {
                var rawRect = new RectFeet(tbBox.Min, tbBox.Max);
                result.Region = new PlaceableRegion(TitleBlockDetectionMode.Detected, rawRect,
                    "Detected (margins too large — ignored)");
                return result;
            }

            var rect = new RectFeet(insetMin, insetMax);
            result.Region = new PlaceableRegion(TitleBlockDetectionMode.Detected, rect, "Detected");
            return result;
        }

        /// <summary>Builds a Manual-mode single-rect region from user-entered mm coordinates (sheet space). No margin inset applied — the user's numbers are used exactly as entered.</summary>
        public PlaceableRegion BuildManualRegion(double minXMm, double minYMm, double maxXMm, double maxYMm)
        {
            var min = new XYZ(minXMm * MmToFeet, minYMm * MmToFeet, 0);
            var max = new XYZ(maxXMm * MmToFeet, maxYMm * MmToFeet, 0);
            var rect = new RectFeet(min, max);
            return new PlaceableRegion(TitleBlockDetectionMode.Manual, rect, "Manual");
        }
    }
}
