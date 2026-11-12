using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Services
{
    /// <summary>
    /// V022 NEW, REWRITTEN per explicit confirmation (superseding the
    /// original Best-Short-Side-Fit free-rect port — see V022 CHANGELOG
    /// remark below). Implements "Rafi's Algo" as a Master Row -> Column
    /// -> Sub-Row -> Sub-Column recursive climb, confirmed explicitly
    /// step by step:
    ///
    ///   MASTER ROW: height = tallest item still remaining anywhere in
    ///   the list (existing suite convention, same as
    ///   MasterRowBandPackingService / the original Rafi's Algo port).
    ///
    ///   COLUMN (tier 1): at the row's current X cursor, place the
    ///   tallest remaining item that fits (width <= row width - cursorX,
    ///   height <= row height), bottom-aligned to the row's bottom edge.
    ///   Column width = that item's own width (NOT a free-rect search —
    ///   confirmed explicitly).
    ///
    ///   SUB-ROW / SUB-COLUMN CLIMB (tiers 2+, repeats): from the
    ///   column's current top edge upward to the Master Row's top edge,
    ///   repeatedly fill a "sub-row" band WITHIN THE COLUMN'S OWN WIDTH
    ///   BUDGET (confirmed explicitly — sub-row width = column width,
    ///   NOT the full Master Row width). Within one sub-row band,
    ///   multiple remaining items are packed SIDE BY SIDE left-to-right
    ///   if they fit (confirmed explicitly — not strictly one item per
    ///   band), each bottom-aligned to that band's bottom edge; the
    ///   band's height = the tallest item actually placed in it, so the
    ///   next band starts cleanly above all of them. Recursion stops
    ///   when either no remaining item fits the available (columnWidth x
    ///   remainingHeight) budget, or the climb reaches the Master Row's
    ///   top edge (both confirmed explicitly).
    ///
    ///   NEXT COLUMN: cursorX advances to (this column's right edge +
    ///   HGap). The next column starts FRESH at the full Master Row
    ///   height again (confirmed explicitly — not adapted/reduced by the
    ///   previous column's climb).
    ///
    ///   ROW CLOSE: when cursorX reaches the row width, or no remaining
    ///   item fits anywhere in the row, the Master Row closes and the
    ///   baseline advances by rowHeight + VGap for the next Master Row.
    ///
    /// V022 CHANGELOG — first port (superseded): originally implemented
    /// as Best-Short-Side-Fit + 4-way-split free-rectangle packing within
    /// each row's active space, since the spec doc's own footnote flagged
    /// its "8-tier Columns -> Sub-Rows -> Sub-Columns" description as an
    /// UNVERIFIED assumption that could not be checked against live
    /// source at the time. The actual structure was subsequently
    /// specified explicitly and confirmed line-by-line (Master Row height
    /// source; column width source; sub-row width scope; side-by-side
    /// sub-row packing; recursion stop condition; next-column start
    /// state) — this rewrite replaces that first port entirely per that
    /// confirmation.
    ///
    /// Returns the same Result/Placement shape as
    /// MasterRowBandPackingService / FreeRectPackingService so
    /// SheetOrderPackingService's downstream conversion and
    /// overflow-relocation logic is shared unchanged.
    /// </summary>
    public class RafisAlgoPackingService
    {
        private const double MmToFeet = 1.0 / 304.8;

        public class Placement
        {
            public ViewOnSheetItem Item { get; set; } = null!;
            /// <summary>Top-left X, sheet space feet, relative to the region origin.</summary>
            public double X { get; set; }
            /// <summary>Top-left Y (top-based, 0 = region top, growing downward in this intermediate space).</summary>
            public double Y { get; set; }
        }

        public class Result
        {
            public List<Placement> Placed { get; set; } = new();
            public List<ViewOnSheetItem> Unplaced { get; set; } = new();
            /// <summary>Total row-space height consumed (top-based), feet.</summary>
            public double TotalHeightFeet { get; set; }
        }

        public Result Pack(List<ViewOnSheetItem> items, double containerWidthFeet, double containerHeightMaxFeet, double hGapFeet, double vGapFeet)
        {
            var remaining = new List<ViewOnSheetItem>(items);
            var placed = new List<Placement>();
            var placedIds = new HashSet<ElementId>();
            double baseline = 0.0; // top-based: 0 = region top, grows downward as Master Rows are consumed

            while (remaining.Count > 0)
            {
                // ── MASTER ROW setup: height = tallest item still remaining anywhere ──
                double rowHeight = remaining.Max(i => i.HeightMm) * MmToFeet;
                double rowTopY = baseline;                 // top-based: smaller Y = higher up
                double rowBottomY = baseline + rowHeight;   // row's bottom edge, in top-based space

                if (rowBottomY > containerHeightMaxFeet + 1e-9)
                    break; // no vertical space left for even this row — remaining items -> overflow (caller relocates)

                double cursorX = 0.0;
                bool placedAnyThisRow = false;

                // ── COLUMN loop: walk left-to-right across the row ──
                while (cursorX < containerWidthFeet - 1e-9)
                {
                    // Tier 1 — COLUMN: tallest remaining item that fits (width, height) at this X.
                    var columnAnchor = remaining
                        .Where(i => !placedIds.Contains(i.ViewportId))
                        .Where(i => (i.WidthMm * MmToFeet) <= (containerWidthFeet - cursorX) + 1e-9
                                 && (i.HeightMm * MmToFeet) <= rowHeight + 1e-9)
                        .OrderByDescending(i => i.HeightMm)
                        .FirstOrDefault();

                    if (columnAnchor == null)
                        break; // nothing left fits this row at this X (or anywhere further right) — close the row

                    double columnWidth = columnAnchor.WidthMm * MmToFeet;
                    double anchorHeight = columnAnchor.HeightMm * MmToFeet;

                    // Place the anchor bottom-aligned to the row's bottom edge.
                    placed.Add(new Placement { Item = columnAnchor, X = cursorX, Y = rowBottomY - anchorHeight });
                    placedIds.Add(columnAnchor.ViewportId);
                    placedAnyThisRow = true;

                    // Tiers 2+ — SUB-ROW / SUB-COLUMN CLIMB: from the anchor's top edge upward to
                    // the Master Row's top edge, repeatedly filling a sub-row band within the SAME
                    // columnWidth budget. Each band packs items SIDE BY SIDE (confirmed explicitly).
                    double climbTopY = rowBottomY - anchorHeight; // top edge reached so far

                    while (climbTopY > rowTopY + 1e-9)
                    {
                        double availableHeight = climbTopY - rowTopY;

                        // Sub-row band: pack remaining items left-to-right within columnWidth,
                        // tallest-first (consistent with the anchor/column ordering convention),
                        // each constrained to availableHeight. Band height = tallest item actually
                        // placed in this band, so the next band starts cleanly above all of them.
                        double subCursorX = cursorX;
                        double bandRemainingWidth = columnWidth;
                        double bandHeight = 0.0;
                        bool placedAnyInBand = false;

                        var bandCandidates = remaining
                            .Where(i => !placedIds.Contains(i.ViewportId))
                            .Where(i => (i.HeightMm * MmToFeet) <= availableHeight + 1e-9)
                            .OrderByDescending(i => i.HeightMm)
                            .ToList();

                        foreach (var candidate in bandCandidates)
                        {
                            double cw = candidate.WidthMm * MmToFeet;
                            double ch = candidate.HeightMm * MmToFeet;
                            double advance = placedAnyInBand ? cw + hGapFeet : cw;

                            if (advance > bandRemainingWidth + 1e-9)
                                continue; // doesn't fit remaining width in this band — try the next (shorter-or-equal) candidate

                            placed.Add(new Placement { Item = candidate, X = subCursorX, Y = climbTopY - ch });
                            placedIds.Add(candidate.ViewportId);

                            subCursorX += advance;
                            bandRemainingWidth -= advance;
                            bandHeight = Math.Max(bandHeight, ch);
                            placedAnyInBand = true;
                        }

                        if (!placedAnyInBand)
                            break; // nothing fits this band at all — column exhausted

                        climbTopY -= (bandHeight + vGapFeet);
                    }

                    cursorX += columnWidth + hGapFeet;
                }

                remaining = remaining.Where(i => !placedIds.Contains(i.ViewportId)).ToList();

                if (!placedAnyThisRow)
                    break; // nothing placed this row at all — avoid an infinite loop, remaining -> overflow

                // ── ROW CLOSE: advance baseline for the next Master Row ──
                baseline = rowBottomY + vGapFeet;
            }

            double totalHeightUsed = placed.Count > 0 ? placed.Max(p => p.Y + p.Item.HeightMm * MmToFeet) : 0;
            return new Result { Placed = placed, Unplaced = remaining, TotalHeightFeet = totalHeightUsed };
        }
    }
}
