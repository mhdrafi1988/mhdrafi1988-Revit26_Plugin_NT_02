using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Services
{
    /// <summary>
    /// Puts viewports in reading order: bands them into rows by bottom edge, then
    /// left-to-right within each row, top row first. No Revit API — pure arithmetic
    /// over BubbleRow, so it is testable without a model (ARCHITECTURE.md §3).
    ///
    /// CHANGED in V009. V008 banded in FilteredElementCollector order, comparing each
    /// viewport against the band's first-seen member:
    ///
    ///     foreach (var e in items)                       // arbitrary order
    ///         for (int i = 0; i &lt; rows.Count; i++)
    ///             if (Math.Abs(bottomY - rowBands[i]) &lt;= thresholdFt) …
    ///
    /// Anchoring on the first member was right; starting from an arbitrary order was
    /// not. Which viewport a band anchors on decides which near-boundary neighbours
    /// join it, so the banding — and the numbering — was a function of collector order
    /// rather than of the drawing. Three viewports 0.06 apart with a 0.065 threshold,
    /// X not increasing with Y, give three different numbering orders depending only
    /// on which one the collector returned first:
    ///
    ///     bottom Y / centre X:  TOP 10.12/5.0   MID 10.06/1.0   LOW 10.00/3.0
    ///     collector TOP,MID,LOW → MID, TOP, LOW
    ///     collector MID,TOP,LOW → MID, LOW, TOP     (all three banded together)
    ///     collector LOW,TOP,MID → TOP, MID, LOW
    ///
    /// V009 sorts by BottomY first and sweeps downward, so the topmost viewport always
    /// anchors the first band and the result depends on geometry alone — the case above
    /// gives MID, TOP, LOW whatever order the collector hands them over in.
    ///
    /// Note for anyone comparing the two: V008 did NOT suffer band "creep" down a
    /// column of sub-threshold steps. Because rowBands[i] stays at the first member's
    /// Y and is never updated, a staircase splits into bands in both versions.
    /// </summary>
    public static class RowOrderingService
    {
        /// <summary>
        /// Reading order for <paramref name="rows"/>. <paramref name="thresholdFt"/> is
        /// how far two bottom edges may differ and still count as the same row.
        /// </summary>
        public static List<BubbleRow> Order(IEnumerable<BubbleRow> rows, double thresholdFt)
        {
            if (rows == null) return new List<BubbleRow>();

            // Top of the sheet is the highest Y, so descending Y is top → bottom.
            // ViewId breaks exact ties so the order is stable for identical geometry.
            List<BubbleRow> byHeight = rows
                .OrderByDescending(r => r.BottomY)
                .ThenBy(r => r.CenterX)
                .ThenBy(r => r.ViewId)
                .ToList();

            if (byHeight.Count == 0) return byHeight;

            double tolerance = thresholdFt > 0 ? thresholdFt : 0;
            var bands = new List<List<BubbleRow>>();
            var current = new List<BubbleRow> { byHeight[0] };

            // Anchor on the band's topmost member: a band accepts rows within the
            // tolerance OF THE ANCHOR, so it cannot creep down the sheet one
            // sub-threshold step at a time.
            double anchorY = byHeight[0].BottomY;

            for (int i = 1; i < byHeight.Count; i++)
            {
                BubbleRow row = byHeight[i];

                if (anchorY - row.BottomY <= tolerance)
                {
                    current.Add(row);
                }
                else
                {
                    bands.Add(current);
                    current = new List<BubbleRow> { row };
                    anchorY = row.BottomY;
                }
            }

            bands.Add(current);

            var ordered = new List<BubbleRow>(byHeight.Count);

            foreach (List<BubbleRow> band in bands)
            {
                ordered.AddRange(band
                    .OrderBy(r => r.CenterX)
                    .ThenByDescending(r => r.BottomY)
                    .ThenBy(r => r.ViewId));
            }

            return ordered;
        }
    }
}
