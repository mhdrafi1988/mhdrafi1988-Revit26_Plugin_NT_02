using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services
{
    /// <summary>
    /// Measures the margins and gaps the sheet already has, so that aligning keeps the look the
    /// user drew instead of imposing new numbers. Revit-free and unit tested.
    /// </summary>
    /// <remarks>
    /// The margins come from the views, not from the title block's own graphics: Revit gives no way
    /// to tell where a title block's title strip starts, so the right margin reads as "where your
    /// views stop", which is normally just inside the strip. Gaps are medians for the same reason
    /// the shared line is (see <see cref="AxisAlign.Median"/>): one unusually wide gap should not
    /// become the sheet's minimum.
    /// </remarks>
    public static class MarginDetector
    {
        /// <summary>
        /// Measures <paramref name="footprints"/> inside <paramref name="frame"/>. Every result is
        /// in millimetres, rounded to <paramref name="roundToMm"/> when that is above zero.
        /// </summary>
        /// <param name="frame">The title block's bounding box, in sheet feet.</param>
        /// <param name="footprints">The current footprints to measure, in sheet feet.</param>
        /// <param name="rowToleranceFeet">Row tolerance, so gaps are measured within a row and between rows.</param>
        /// <param name="roundToMm">Rounding step in mm; 0 or less returns the measured values.</param>
        public static DetectedLayout Detect(LayoutRect frame, IReadOnlyList<LayoutRect> footprints,
            double rowToleranceFeet, double roundToMm)
        {
            if (footprints == null || footprints.Count == 0 || !frame.IsValid)
                return new DetectedLayout { HasValues = false };

            double Round(double mm) => Units.RoundToStep(mm, roundToMm);

            var boxes = footprints.Select((rect, i) => new TrackBox(i, rect)).ToList();
            var rows = TrackDetection.DetectRows(boxes, rowToleranceFeet);

            return new DetectedLayout
            {
                HasValues = true,
                MarginLeftMm = Round(Units.FeetToMm(footprints.Min(f => f.MinX) - frame.MinX)),
                MarginRightMm = Round(Units.FeetToMm(frame.MaxX - footprints.Max(f => f.MaxX))),
                MarginBottomMm = Round(Units.FeetToMm(footprints.Min(f => f.MinY) - frame.MinY)),
                MarginTopMm = Round(Units.FeetToMm(frame.MaxY - footprints.Max(f => f.MaxY))),
                GapHorizontalMm = MedianGapMm(HorizontalGaps(rows, footprints), roundToMm),
                GapVerticalMm = MedianGapMm(VerticalGaps(rows, footprints), roundToMm)
            };
        }

        /// <summary>Gaps between neighbours inside each row, in feet. Overlapping neighbours are left out.</summary>
        private static IEnumerable<double> HorizontalGaps(List<List<int>> rows, IReadOnlyList<LayoutRect> footprints)
        {
            foreach (var row in rows)
            {
                var ordered = row.OrderBy(i => footprints[i].MinX).ToList();
                for (int i = 0; i + 1 < ordered.Count; i++)
                {
                    double gap = footprints[ordered[i + 1]].MinX - footprints[ordered[i]].MaxX;
                    if (gap > 0) yield return gap;
                }
            }
        }

        /// <summary>Gaps between neighbouring rows, in feet. Rows that overlap vertically are left out.</summary>
        private static IEnumerable<double> VerticalGaps(List<List<int>> rows, IReadOnlyList<LayoutRect> footprints)
        {
            // DetectRows gives rows top first, so the next row down is the next entry.
            for (int r = 0; r + 1 < rows.Count; r++)
            {
                double thisRowBottom = rows[r].Min(i => footprints[i].MinY);
                double nextRowTop = rows[r + 1].Max(i => footprints[i].MaxY);
                double gap = thisRowBottom - nextRowTop;
                if (gap > 0) yield return gap;
            }
        }

        private static double? MedianGapMm(IEnumerable<double> gapsFeet, double roundToMm)
        {
            var gaps = gapsFeet.ToList();
            if (gaps.Count == 0) return null;
            return Units.RoundToStep(Units.FeetToMm(AxisAlign.Median(gaps)), roundToMm);
        }
    }
}
