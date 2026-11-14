using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Services
{
    /// <summary>
    /// V022 NEW. Implements the 3 "no banding" strategies from the
    /// "Combined Packing Strategy Spec" doc that pack directly into the
    /// whole region in one pass, with no Master Row/Band scaffolding:
    /// Max Fill, Skyline, and Skyline + Waste Map. (RafisAlgo is a
    /// sibling service, RafisAlgoPackingService, since it still retains
    /// its own row/baseline progression — see that class for why it's
    /// separate.)
    ///
    /// Returns the same Result/Placement shape as
    /// MasterRowBandPackingService so SheetOrderPackingService's
    /// downstream conversion-to-sheet-space and overflow-relocation logic
    /// is shared unchanged across all 7 strategies — top-based intermediate
    /// space, row 0 / Y=0 at the region's top edge, Y growing downward.
    ///
    /// ASSUMPTION (flagged): "existing order" for item placement order is
    /// used as given (no re-sort) for Skyline per the spec's "TBD" note —
    /// only Max Fill's ascending-area sort is spec-mandated. If a
    /// different default order is wanted for Skyline, that's a one-line
    /// change in PackSkyline below.
    /// </summary>
    public class FreeRectPackingService
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

        private class FreeRect
        {
            public double X, Y, W, H;
        }

        public Result Pack(
            List<ViewOnSheetItem> items,
            double containerWidthFeet,
            double containerHeightMaxFeet,
            double hGapFeet,
            double vGapFeet,
            RowFillStrategy strategy)
        {
            return strategy switch
            {
                RowFillStrategy.MaxFill => PackMaxFill(items, containerWidthFeet, containerHeightMaxFeet, hGapFeet, vGapFeet),
                RowFillStrategy.Skyline => PackSkyline(items, containerWidthFeet, containerHeightMaxFeet, hGapFeet, vGapFeet, useWasteMap: false),
                RowFillStrategy.SkylineWasteMap => PackSkyline(items, containerWidthFeet, containerHeightMaxFeet, hGapFeet, vGapFeet, useWasteMap: true),
                _ => throw new ArgumentOutOfRangeException(nameof(strategy))
            };
        }

        // ============================================================
        // MAX FILL
        // Spec: items sorted ascending by area (smallest first), single
        // free-rect list spanning the whole region, Best-Short-Side-Fit,
        // ties -> candidate with MAX remaining free area after split,
        // up to 4-way split (same split primitive as MaxRects).
        // ============================================================
        private Result PackMaxFill(List<ViewOnSheetItem> items, double containerW, double containerHMax, double hGap, double vGap)
        {
            var sorted = items.OrderBy(i => i.WidthMm * i.HeightMm).ToList();
            var freeRects = new List<FreeRect> { new FreeRect { X = 0, Y = 0, W = containerW, H = containerHMax } };
            var placed = new List<Placement>();
            var unplaced = new List<ViewOnSheetItem>();
            double maxYUsed = 0;

            foreach (var it in sorted)
            {
                double w = it.WidthMm * MmToFeet;
                double h = it.HeightMm * MmToFeet;

                int bestIdx = -1;
                double bestScore = double.MaxValue;
                double bestRemainingArea = double.MinValue;

                for (int idx = 0; idx < freeRects.Count; idx++)
                {
                    var fr = freeRects[idx];
                    if (w <= fr.W + 1e-9 && h <= fr.H + 1e-9)
                    {
                        double score = Math.Min(fr.W - w, fr.H - h); // Best-Short-Side-Fit
                        double remainingArea = (fr.W * fr.H) - (w * h); // tie-break: max remaining free area after split

                        if (score < bestScore - 1e-9 ||
                            (Math.Abs(score - bestScore) <= 1e-9 && remainingArea > bestRemainingArea))
                        {
                            bestScore = score;
                            bestRemainingArea = remainingArea;
                            bestIdx = idx;
                        }
                    }
                }

                if (bestIdx < 0)
                {
                    unplaced.Add(it); // doesn't fit any free rect — not overflow yet, caller relocates
                    continue;
                }

                var chosen = freeRects[bestIdx];
                freeRects.RemoveAt(bestIdx);

                double itemX = chosen.X;
                double itemY = chosen.Y; // top-aligned within the free rect (top-based space)

                placed.Add(new Placement { Item = it, X = itemX, Y = itemY });
                maxYUsed = Math.Max(maxYUsed, itemY + h);

                double px1 = itemX, py1 = itemY;
                double px2 = itemX + w + hGap, py2 = itemY + h + vGap;

                // 4-way split against the consumed rect AND every other free rect still in the list.
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

            return new Result { Placed = placed, Unplaced = unplaced, TotalHeightFeet = maxYUsed };
        }

        // ============================================================
        // SKYLINE (BOTTOM-LEFT) and SKYLINE + WASTE MAP
        // Spec: 1D height profile (one value per x-column across
        // containerW), each item scans for the x-position minimizing the
        // resulting profile height under its footprint, placed there,
        // profile raised. Waste-map variant additionally captures small
        // gap rects left below the profile and checks them first.
        //
        // Profile discretization: sampled per-item-edge rather than a
        // fixed pixel grid — for each item, only the x-positions where
        // some existing segment's start/end could matter are true
        // candidates, but per spec simplicity + this tool's item counts
        // (tens, not thousands) a direct scan over existing segment
        // boundaries is used (see BuildCandidateXs) rather than a fixed
        // 1mm/1px grid, to stay exact rather than approximate.
        // ============================================================
        private class SkylineSegment
        {
            public double X, W, Height; // Height = top-based Y (0 = region top), i.e. how far down the profile currently sits at this segment
        }

        private Result PackSkyline(List<ViewOnSheetItem> items, double containerW, double containerHMax, double hGap, double vGap, bool useWasteMap)
        {
            // Profile starts flush at the region's top edge (Y=0, top-based space) — tracks how far
            // DOWN the packed area currently extends at each x-column, growing toward containerHMax.
            var profile = new List<SkylineSegment> { new SkylineSegment { X = 0, W = containerW, Height = 0 } };
            var wasteMap = new List<FreeRect>();
            var placed = new List<Placement>();
            var unplaced = new List<ViewOnSheetItem>();
            double maxYUsed = 0;

            // Existing order per spec's "TBD" note — see class remarks.
            foreach (var it in items)
            {
                double w = it.WidthMm * MmToFeet;
                double h = it.HeightMm * MmToFeet;

                bool placedThisItem = false;

                // Waste-map check first (Skyline+WasteMap only).
                if (useWasteMap)
                {
                    var wasteCandidate = wasteMap.FirstOrDefault(r => w <= r.W + 1e-9 && h <= r.H + 1e-9);
                    if (wasteCandidate != null)
                    {
                        placed.Add(new Placement { Item = it, X = wasteCandidate.X, Y = wasteCandidate.Y });
                        maxYUsed = Math.Max(maxYUsed, wasteCandidate.Y + h);
                        wasteMap.Remove(wasteCandidate);
                        placedThisItem = true;
                    }
                }

                if (!placedThisItem)
                {
                    double bestX = -1, bestY = double.MaxValue;
                    foreach (double candidateX in BuildCandidateXs(profile, containerW, w))
                    {
                        double y = MaxHeightUnder(profile, candidateX, w); // deepest point (top-based: largest Y) under the item's footprint
                        if (y < bestY - 1e-9)
                        {
                            bestY = y;
                            bestX = candidateX;
                        }
                    }

                    if (bestX < 0 || bestY + h > containerHMax + 1e-9)
                    {
                        unplaced.Add(it); // doesn't fit — not overflow yet, caller relocates
                        continue;
                    }

                    // Waste map: capture gap rects below the new segment where neighboring
                    // columns were already deeper (taller stack) than bestY, before we raise the profile.
                    if (useWasteMap)
                    {
                        foreach (var gap in FindGapsUnder(profile, bestX, w, bestY))
                            wasteMap.Add(gap);
                    }

                    placed.Add(new Placement { Item = it, X = bestX, Y = bestY });
                    maxYUsed = Math.Max(maxYUsed, bestY + h);

                    UpdateSkyline(profile, bestX, w, bestY + h + vGap, containerW);
                }
            }

            return new Result { Placed = placed, Unplaced = unplaced, TotalHeightFeet = maxYUsed };
        }

        /// <summary>Candidate x-positions: 0, and every existing segment boundary where the item still fits within containerW.</summary>
        private static IEnumerable<double> BuildCandidateXs(List<SkylineSegment> profile, double containerW, double itemW)
        {
            var xs = new SortedSet<double>();
            foreach (var seg in profile)
            {
                if (seg.X + itemW <= containerW + 1e-9) xs.Add(seg.X);
                double segEnd = seg.X + seg.W;
                double alt = segEnd; // right edge of this segment as a candidate left-edge for the next item
                if (alt + itemW <= containerW + 1e-9) xs.Add(alt);
            }
            if (xs.Count == 0 && itemW <= containerW + 1e-9) xs.Add(0);
            return xs;
        }

        /// <summary>Deepest (max) profile height under [x, x+itemW).</summary>
        private static double MaxHeightUnder(List<SkylineSegment> profile, double x, double itemW)
        {
            double maxH = 0;
            double x2 = x + itemW;
            foreach (var seg in profile)
            {
                double segEnd = seg.X + seg.W;
                bool overlaps = seg.X < x2 - 1e-9 && segEnd > x + 1e-9;
                if (overlaps) maxH = Math.Max(maxH, seg.Height);
            }
            return maxH;
        }

        /// <summary>Gap rects under [x, x+itemW) where the existing profile was already deeper than placedY (waste pockets sealed off by this placement).</summary>
        private static List<FreeRect> FindGapsUnder(List<SkylineSegment> profile, double x, double itemW, double placedY)
        {
            var gaps = new List<FreeRect>();
            double x2 = x + itemW;
            foreach (var seg in profile)
            {
                double segEnd = seg.X + seg.W;
                double left = Math.Max(seg.X, x);
                double right = Math.Min(segEnd, x2);
                if (right <= left + 1e-9) continue;

                if (seg.Height > placedY + 1e-9)
                {
                    // this column was already deeper than where we're placing — the strip
                    // between placedY and seg.Height under [left,right) becomes a waste pocket.
                    gaps.Add(new FreeRect { X = left, Y = placedY, W = right - left, H = seg.Height - placedY });
                }
            }
            return gaps;
        }

        /// <summary>Raises the profile under [x, x+itemW) to newHeight, splitting/merging segments as needed.</summary>
        private static void UpdateSkyline(List<SkylineSegment> profile, double x, double itemW, double newHeight, double containerW)
        {
            double x2 = Math.Min(x + itemW, containerW);
            var result = new List<SkylineSegment>();

            foreach (var seg in profile)
            {
                double segEnd = seg.X + seg.W;

                if (segEnd <= x + 1e-9 || seg.X >= x2 - 1e-9)
                {
                    result.Add(seg); // no overlap
                    continue;
                }

                if (seg.X < x - 1e-9)
                    result.Add(new SkylineSegment { X = seg.X, W = x - seg.X, Height = seg.Height }); // left remainder, unchanged

                if (segEnd > x2 + 1e-9)
                    result.Add(new SkylineSegment { X = x2, W = segEnd - x2, Height = seg.Height }); // right remainder, unchanged

                // overlapped middle portion is replaced by the new segment below (added once, after the loop)
            }

            result.Add(new SkylineSegment { X = x, W = x2 - x, Height = newHeight });

            // Merge adjacent same-height segments and sort by X for the next scan.
            var merged = result.Where(s => s.W > 1e-9).OrderBy(s => s.X).ToList();
            profile.Clear();
            foreach (var seg in merged)
            {
                if (profile.Count > 0 && Math.Abs(profile[^1].Height - seg.Height) < 1e-9 && Math.Abs((profile[^1].X + profile[^1].W) - seg.X) < 1e-6)
                    profile[^1].W += seg.W;
                else
                    profile.Add(seg);
            }
        }

        private static bool Overlaps(double ax1, double ay1, double ax2, double ay2, FreeRect b)
        {
            double bx1 = b.X, by1 = b.Y, bx2 = b.X + b.W, by2 = b.Y + b.H;
            return !(ax2 <= bx1 + 1e-9 || ax1 >= bx2 - 1e-9 || ay2 <= by1 + 1e-9 || ay1 >= by2 - 1e-9);
        }

        /// <summary>4-way split of a free rect against an overlapping placed-item footprint (top-based Y). Discards degenerate pieces. Same primitive as MasterRowBandPackingService's MaxRects split.</summary>
        private static List<FreeRect> SplitAgainst(FreeRect fr, double px1, double py1, double px2, double py2)
        {
            var pieces = new List<FreeRect>();

            if (fr.X < px1) // left piece
                pieces.Add(new FreeRect { X = fr.X, Y = fr.Y, W = px1 - fr.X, H = fr.H });

            if (fr.X + fr.W > px2) // right piece
                pieces.Add(new FreeRect { X = px2, Y = fr.Y, W = (fr.X + fr.W) - px2, H = fr.H });

            if (fr.Y < py1) // top piece (top-based space: smaller Y = higher up)
                pieces.Add(new FreeRect { X = fr.X, Y = fr.Y, W = fr.W, H = py1 - fr.Y });

            if (fr.Y + fr.H > py2) // bottom piece
                pieces.Add(new FreeRect { X = fr.X, Y = py2, W = fr.W, H = (fr.Y + fr.H) - py2 });

            return pieces.Where(p => p.W > 1e-9 && p.H > 1e-9).ToList();
        }
    }
}
