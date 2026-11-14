using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Services
{
    /// <summary>
    /// V014 NEW: replaces ShelfPackingService entirely. Implements the
    /// user-supplied "Master Row + Band Rectangle Packing — All Three Fill
    /// Strategies" spec verbatim, including its known row-height plateau
    /// characteristic (see remarks below) — ported AS SPECIFIED, unfixed,
    /// per explicit request ("port as-specified, I want to test it myself").
    ///
    /// SHARED SCAFFOLDING (identical for all three strategies):
    /// Items are packed row by row into "Master Rows." Each Master Row's
    /// height is the tallest height among ALL items still unplaced
    /// anywhere in the input list — not just the items that end up placed
    /// in that specific row. Every remaining item is then assigned to a
    /// height Band based on its ratio to that row height:
    ///   Band 1: ratio >= 0.75   Band 2: ratio >= 0.50
    ///   Band 3: ratio >= 0.25   Band 4: everything below 0.25
    /// (V021: thresholds updated per explicit request — Band 1 no longer
    /// distinguishes an exact row-height match as its own tier; boundary
    /// ties go to the higher band.)
    /// (Shelf strategy only, additionally: each band gets a temporary
    /// placement ceiling = tallest item's height WITHIN that band, locked
    /// for the row — bookkeeping only, items are never resized.)
    ///
    /// KNOWN CHARACTERISTIC (not a bug — this is the spec as given): because
    /// row height is picked from the ENTIRE remaining pool rather than the
    /// row's own contents, a small number of unusually tall items in an
    /// otherwise short dataset can force several consecutive Master Rows to
    /// all use that same tall row height, even in rows where those tall
    /// items don't actually get placed — wasting vertical space for the
    /// many shorter items sharing that row. Confirmed in testing: this can
    /// cause Guillotine and MaxRects to plateau at identical placed-counts
    /// on such datasets, since both exhaust container height before their
    /// smarter free-rectangle logic gets a chance to work on the many
    /// shorter leftover items. Flag to the user if this shows up in testing.
    ///
    /// All three strategies place items bottom-aligned within their row
    /// (itemY = freeRect/row top + freeRect/row height - itemHeight),
    /// independent of the SheetOrderPackingService.RowAlignment setting —
    /// that setting only applies to the separate, simpler row-packing tail
    /// in SheetOrderPackingService for any items this service could not
    /// place. Uniform `gap` (feet) is applied on every relevant edge per
    /// spec — this service reads GapSettings.GlobalHorizontalGapMm for
    /// both axes per explicit request ("keep separate H/V gap fields, just
    /// feed the same value to both internally") — i.e. H and V gap fields
    /// remain separate in the UI/settings, but THIS algorithm's single
    /// `gap` parameter uses the Horizontal value for both axes. If the user
    /// later wants H and V to diverge for this algorithm specifically, the
    /// spec itself only supports one uniform gap — would need a spec change.
    /// </summary>
    public class MasterRowBandPackingService
    {
        private const double MmToFeet = 1.0 / 304.8;

        public class Placement
        {
            public ViewOnSheetItem Item { get; set; } = null!;
            /// <summary>Top-left X, sheet space feet, relative to the row-packing origin (region.LargeRect.Min.X / row top).</summary>
            public double X { get; set; }
            /// <summary>Top-left Y (top-based, row 0 starts at 0 and grows downward in this intermediate space — converted to sheet Y by the caller).</summary>
            public double Y { get; set; }
            public int Band { get; set; }
            public string BandLabel { get; set; } = "";
        }

        public class Result
        {
            public List<Placement> Placed { get; set; } = new();
            public List<ViewOnSheetItem> Unplaced { get; set; } = new();
            /// <summary>Total row-space height consumed (top-based), feet — caller uses this to know where any leftover/simple packing should resume.</summary>
            public double TotalHeightFeet { get; set; }
        }

        private class FreeRect
        {
            public double X, Y, W, H;
        }

        /// <summary>
        /// Runs the selected fill strategy. containerWidthFeet is fixed;
        /// containerHeightMaxFeet is a CAP (spec says container height "may
        /// grow if items don't fit" — this port caps it to the region's
        /// usable height and reports leftover items as Unplaced rather than
        /// growing the sheet, consistent with PlaceWhatsPlaceable overflow
        /// handling elsewhere in this tool).
        /// </summary>
        public Result Pack(
            List<ViewOnSheetItem> items,
            double containerWidthFeet,
            double containerHeightMaxFeet,
            double gapFeet,
            RowFillStrategy strategy)
        {
            return strategy switch
            {
                RowFillStrategy.Shelf => PackShelf(items, containerWidthFeet, containerHeightMaxFeet, gapFeet),
                RowFillStrategy.Guillotine => PackFreeRect(items, containerWidthFeet, containerHeightMaxFeet, gapFeet, useShortSideFit: false),
                RowFillStrategy.MaxRects => PackFreeRect(items, containerWidthFeet, containerHeightMaxFeet, gapFeet, useShortSideFit: true),
                _ => throw new ArgumentOutOfRangeException(nameof(strategy))
            };
        }

        // ── Shared Step 1: banding ──────────────────────────────────────
        // V021 CHANGE: band thresholds updated per explicit request —
        // Band 1: ratio >= 0.75 (was >= 1.00 — the old "exact row-height
        // match" top tier no longer exists; items >=0.75 are now all
        // Band 1). Band 2: 0.50–0.75. Band 3: 0.25–0.50. Band 4: < 0.25
        // (was < 0.50). Boundary ties go to the higher band (>= on each
        // threshold), confirmed explicitly.
        private static int BandOf(double heightFeet, double rowHeightFeet)
        {
            double ratio = heightFeet / rowHeightFeet;
            if (ratio >= 0.75) return 1;
            if (ratio >= 0.50) return 2;
            if (ratio >= 0.25) return 3;
            return 4;
        }

        /// <summary>
        /// Shared Step 1, in full: picks this Master Row's height (tallest
        /// item anywhere still in `remaining` — see class remarks on the
        /// row-height plateau characteristic) and assigns every remaining
        /// item to a height Band. Was previously duplicated verbatim in
        /// both PackShelf and PackFreeRect; extracted here since all three
        /// strategies share this exact step per spec.
        /// </summary>
        private static (double rowHeight, Dictionary<int, List<ViewOnSheetItem>> bands) StartMasterRow(List<ViewOnSheetItem> remaining)
        {
            double rowHeight = remaining.Max(i => i.HeightMm * MmToFeet);

            var bands = new Dictionary<int, List<ViewOnSheetItem>>();
            foreach (var it in remaining)
            {
                int b = BandOf(it.HeightMm * MmToFeet, rowHeight);
                if (!bands.TryGetValue(b, out var list)) { list = new List<ViewOnSheetItem>(); bands[b] = list; }
                list.Add(it);
            }

            return (rowHeight, bands);
        }

        /// <summary>
        /// Shelf strategy only — Steps 3 and 4 both repeatedly group a pool
        /// of leftover items by matching width to build "columns" stacked
        /// bottom-aligned to rowBottom, differing only in (a) the height
        /// budget a column may fill up to (Step 3: that band's temporary
        /// ceiling; Step 4: the full row height) and (b) the Band tag
        /// stamped on each placement. Extracted here since the two call
        /// sites were previously identical copy-pasted blocks.
        ///
        /// Mutates `placed` and `placedThisRow` as a side effect (adds
        /// entries as columns are placed) and returns the advanced x
        /// cursor. Does NOT mutate `pool` — the caller re-filters its own
        /// pool/band lists against `placedThisRow` afterward, same as
        /// before extraction.
        /// </summary>
        private double PackColumnsIntoRow(
            List<ViewOnSheetItem> pool,
            List<Placement> placed,
            HashSet<ElementId> placedThisRow,
            double x,
            double containerW,
            double rowBottom,
            double gap,
            double heightBudget,
            int bandTag,
            string bandLabel)
        {
            var remainingPool = pool;

            while (remainingPool.Count > 0 && x < containerW - 1e-9)
            {
                var widthGroups = remainingPool.GroupBy(i => i.WidthMm).OrderByDescending(g => g.Count()).ToList();
                double? colWidthMm = null;
                List<ViewOnSheetItem>? colItemsAvail = null;
                foreach (var g in widthGroups)
                {
                    double wFeet = g.Key * MmToFeet;
                    if (x + wFeet <= containerW + 1e-9)
                    {
                        colWidthMm = g.Key;
                        colItemsAvail = g.ToList();
                        break;
                    }
                }
                if (colWidthMm == null) break;

                var stacked = new List<ViewOnSheetItem>();
                double stackedH = 0.0;
                foreach (var it in colItemsAvail!)
                {
                    double h = it.HeightMm * MmToFeet;
                    double addH = h + (stacked.Count > 0 ? gap : 0);
                    if (stackedH + addH <= heightBudget + 1e-9)
                    {
                        stacked.Add(it);
                        stackedH += addH;
                    }
                    else break;
                }
                if (stacked.Count == 0)
                {
                    remainingPool = remainingPool.Where(i => i.WidthMm != colWidthMm.Value).ToList();
                    continue;
                }

                double colWFeet = colWidthMm.Value * MmToFeet;
                double y = rowBottom;
                foreach (var it in stacked)
                {
                    double h = it.HeightMm * MmToFeet;
                    y -= h;
                    placed.Add(new Placement { Item = it, X = x, Y = y, Band = bandTag, BandLabel = bandLabel });
                    placedThisRow.Add(it.ViewportId);
                    y -= gap;
                }
                remainingPool = remainingPool.Where(i => !placedThisRow.Contains(i.ViewportId)).ToList();
                x += colWFeet + gap;
            }

            return x;
        }

        // ============================================================
        // STRATEGY A — Shelf / Sub-row
        // ============================================================
        private Result PackShelf(List<ViewOnSheetItem> items, double containerW, double containerHMax, double gap)
        {
            var remaining = new List<ViewOnSheetItem>(items);
            var placed = new List<Placement>();
            double rowTop = 0.0;

            while (remaining.Count > 0)
            {
                var (rowHeight, bands) = StartMasterRow(remaining);

                // Step 1.3: per-band temporary ceiling (bookkeeping only), locked for this row
                var bandCeiling = new Dictionary<int, double>();
                foreach (var kvp in bands)
                    bandCeiling[kvp.Key] = kvp.Value.Max(i => i.HeightMm * MmToFeet);

                double rowBottom = rowTop + rowHeight;
                double x = 0.0;
                var placedThisRow = new HashSet<ElementId>();

                // Step 2: place Band 1 left to right
                if (bands.TryGetValue(1, out var band1))
                {
                    band1 = band1.OrderByDescending(i => i.HeightMm).ToList();
                    bool first = true;
                    foreach (var it in band1.ToList())
                    {
                        double w = it.WidthMm * MmToFeet;
                        double h = it.HeightMm * MmToFeet;
                        if (x + w > containerW + 1e-9) break;

                        double y = first ? rowTop : (rowBottom - h); // first: top-left aligned; rest: bottom-aligned
                        first = false;

                        placed.Add(new Placement { Item = it, X = x, Y = y, Band = 1, BandLabel = "1" });
                        placedThisRow.Add(it.ViewportId);
                        x += w + gap;
                    }
                    bands[1] = band1.Where(i => !placedThisRow.Contains(i.ViewportId)).ToList();
                }

                // Step 3: stack Bands 2, 3, 4 as sub-row columns (group by matching width)
                foreach (int b in new[] { 2, 3, 4 })
                {
                    if (!bands.TryGetValue(b, out var pool)) continue;
                    pool = pool.Where(i => !placedThisRow.Contains(i.ViewportId)).ToList();
                    double ceiling = bandCeiling.TryGetValue(b, out var c) ? c : 0;

                    x = PackColumnsIntoRow(pool, placed, placedThisRow, x, containerW, rowBottom, gap, ceiling, b, b.ToString());
                    bands[b] = pool.Where(i => !placedThisRow.Contains(i.ViewportId)).ToList();
                }

                // Step 4: gap-fill pass — pool remaining Band 2/3/4 items, full row-height budget
                var gapfillPool = new List<ViewOnSheetItem>();
                foreach (int b in new[] { 2, 3, 4 })
                    if (bands.TryGetValue(b, out var p)) gapfillPool.AddRange(p);
                gapfillPool = gapfillPool.Where(i => !placedThisRow.Contains(i.ViewportId)).ToList();

                x = PackColumnsIntoRow(gapfillPool, placed, placedThisRow, x, containerW, rowBottom, gap, rowHeight, 0, "gapfill");

                // Shared Step 6: close the row
                if (placedThisRow.Count == 0)
                    break;

                remaining = remaining.Where(i => !placedThisRow.Contains(i.ViewportId)).ToList();
                rowTop = rowBottom + gap;
                if (rowTop > containerHMax)
                    break;
            }

            return new Result { Placed = placed, Unplaced = remaining, TotalHeightFeet = rowTop };
        }

        // ============================================================
        // STRATEGY B (Guillotine, useShortSideFit=false) and
        // STRATEGY C (MaxRects,   useShortSideFit=true)
        // Share identical scaffolding — only the fit score and split
        // behavior differ, exactly as the spec's two strategies do.
        // ============================================================
        private Result PackFreeRect(List<ViewOnSheetItem> items, double containerW, double containerHMax, double gap, bool useShortSideFit)
        {
            var remaining = new List<ViewOnSheetItem>(items);
            var placed = new List<Placement>();
            double rowTop = 0.0;

            while (remaining.Count > 0)
            {
                var (rowHeight, bands) = StartMasterRow(remaining);

                // Step 3: placement order — Band 1 (tallest-first), then 2, 3, 4 (each tallest-first)
                var order = new List<ViewOnSheetItem>();
                foreach (int b in new[] { 1, 2, 3, 4 })
                    if (bands.TryGetValue(b, out var list))
                        order.AddRange(list.OrderByDescending(i => i.HeightMm));

                var freeRects = new List<FreeRect> { new FreeRect { X = 0, Y = rowTop, W = containerW, H = rowHeight } };
                var placedThisRow = new HashSet<ElementId>();

                foreach (var it in order)
                {
                    double w = it.WidthMm * MmToFeet;
                    double h = it.HeightMm * MmToFeet;

                    int bestIdx = -1;
                    double bestScore = double.MaxValue;
                    for (int idx = 0; idx < freeRects.Count; idx++)
                    {
                        var fr = freeRects[idx];
                        if (w <= fr.W + 1e-9 && h <= fr.H + 1e-9)
                        {
                            double score = useShortSideFit
                                ? Math.Min(fr.W - w, fr.H - h)   // MaxRects: Best-Short-Side-Fit
                                : (fr.W - w) + (fr.H - h);       // Guillotine: Best-Area-Fit (combined leftover)
                            if (score < bestScore)
                            {
                                bestScore = score;
                                bestIdx = idx;
                            }
                        }
                    }
                    if (bestIdx < 0) continue; // doesn't fit any free rect — skip, stays in remaining for next row

                    var chosen = freeRects[bestIdx];
                    freeRects.RemoveAt(bestIdx);

                    double itemX = chosen.X;
                    double itemY = chosen.Y + chosen.H - h; // bottom-aligned within the free rect

                    int band = BandOf(h, rowHeight);
                    placed.Add(new Placement { Item = it, X = itemX, Y = itemY, Band = band, BandLabel = band.ToString() });
                    placedThisRow.Add(it.ViewportId);

                    double px1 = itemX, py1 = itemY - gap;
                    double px2 = itemX + w + gap, py2 = itemY + h;

                    if (useShortSideFit)
                    {
                        // MaxRects: 4-way split against the CONSUMED rect (chosen) AND every
                        // other free rect still in the list (spec: "plus the one just consumed").
                        var newFree = new List<FreeRect>();
                        newFree.AddRange(SplitAgainst(chosen, px1, py1, px2, py2));
                        foreach (var existing in freeRects)
                        {
                            if (Overlaps(px1, py1, px2, py2, existing))
                                newFree.AddRange(SplitAgainst(existing, px1, py1, px2, py2));
                            else
                                newFree.Add(existing);
                        }
                        freeRects = newFree;
                    }
                    else
                    {
                        // Guillotine: simple 2-way split of the consumed rect only (right / below).
                        var right = new FreeRect { X = chosen.X + w + gap, Y = chosen.Y, W = chosen.W - w - gap, H = chosen.H };
                        var below = new FreeRect { X = chosen.X, Y = chosen.Y, W = w, H = chosen.H - h - gap };
                        if (right.W > 1e-9 && right.H > 1e-9) freeRects.Add(right);
                        if (below.W > 1e-9 && below.H > 1e-9) freeRects.Add(below);
                    }
                }

                if (placedThisRow.Count == 0)
                    break;

                remaining = remaining.Where(i => !placedThisRow.Contains(i.ViewportId)).ToList();
                rowTop = rowTop + rowHeight + gap;
                if (rowTop > containerHMax)
                    break;
            }

            return new Result { Placed = placed, Unplaced = remaining, TotalHeightFeet = rowTop };
        }

        private static bool Overlaps(double ax1, double ay1, double ax2, double ay2, FreeRect b)
        {
            double bx1 = b.X, by1 = b.Y, bx2 = b.X + b.W, by2 = b.Y + b.H;
            return !(ax2 <= bx1 + 1e-9 || ax1 >= bx2 - 1e-9 || ay2 <= by1 + 1e-9 || ay1 >= by2 - 1e-9);
        }

        /// <summary>4-way split of a free rect against an overlapping placed-item footprint. Discards degenerate (zero/negative) pieces.</summary>
        private static List<FreeRect> SplitAgainst(FreeRect fr, double px1, double py1, double px2, double py2)
        {
            var pieces = new List<FreeRect>();

            if (fr.X < px1) // left piece
                pieces.Add(new FreeRect { X = fr.X, Y = fr.Y, W = px1 - fr.X, H = fr.H });

            if (fr.X + fr.W > px2) // right piece
                pieces.Add(new FreeRect { X = px2, Y = fr.Y, W = (fr.X + fr.W) - px2, H = fr.H });

            if (fr.Y < py1) // bottom piece
                pieces.Add(new FreeRect { X = fr.X, Y = fr.Y, W = fr.W, H = py1 - fr.Y });

            if (fr.Y + fr.H > py2) // top piece
                pieces.Add(new FreeRect { X = fr.X, Y = py2, W = fr.W, H = (fr.Y + fr.H) - py2 });

            return pieces.Where(p => p.W > 1e-9 && p.H > 1e-9).ToList();
        }
    }
}
