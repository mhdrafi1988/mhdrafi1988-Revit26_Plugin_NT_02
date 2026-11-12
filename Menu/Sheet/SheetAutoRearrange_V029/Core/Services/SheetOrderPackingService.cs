using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Services
{
    /// <summary>
    /// Sheet Auto Rearrange's only packing algorithm as of V009 (Reading
    /// Order still exists as a file for potential future use, but is no
    /// longer reachable from the UI — algorithm selection was removed per
    /// explicit request: "only on method is needed (by default)").
    ///
    /// V009 CHANGE: gap between views is now a single global H/V value
    /// (GapSettings.GlobalHorizontalGapMm / GlobalVerticalGapMm) instead of
    /// per-ViewType groups — ResolveGroup/ViewTypeGroupResolver removed
    /// entirely, every gap lookup below reads straight off GapSettings.
    ///
    /// V014 CHANGE: ShelfPackingService (Tall/Wide anchor + L-shape shelf
    /// system) REMOVED entirely, replaced by MasterRowBandPackingService —
    /// see that class's header for the full algorithm description and its
    /// known row-height plateau characteristic (ported as-specified,
    /// unfixed, per explicit request). There is no anchor concept anymore:
    /// every item goes through the same Master Row/Band pipeline, then any
    /// item that service could not place falls through to the simple
    /// row-packing tail below (GroupIntoRows), same as before.
    ///
    /// V014 FIX: overflow items (Fits=false) are now actually relocated
    /// below the titleblock's usable area (largeRect.Min.Y and downward)
    /// instead of keeping whatever in-bounds/overlapping coordinates the
    /// packer had already computed for them. Previously the Warning log
    /// message claimed "placed below the sheet" but nothing enforced that —
    /// an overflow item could sit anywhere the packer left it, including
    /// still inside the usable region overlapping another view. See the
    /// dedicated block near the end of Pack() for the relocation logic.
    ///
    /// V026 CHANGE: Pack() now splits `items` into ordered priority groups
    /// (BuildPriorityGroups) and packs each group in turn into its own
    /// shrinking sub-region via the new PackGroup helper (the pre-V026 body
    /// of Pack(), extracted unchanged) — see BuildPriorityGroups/PackGroup
    /// remarks. With priority grouping off (or no order configured), this
    /// is a single-group no-op: identical output to every version before
    /// V026.
    /// </summary>
    public class SheetOrderPackingService
    {
        private const double MmToFeet = 1.0 / 304.8;

        private readonly MasterRowBandPackingService _masterRowPacker = new();

        // V022 NEW: the 4 non-banded strategies (MaxFill, RafisAlgo,
        // Skyline, SkylineWasteMap) — see FreeRectPackingService /
        // RafisAlgoPackingService for why RafisAlgo is a separate class.
        private readonly FreeRectPackingService _freeRectPacker = new();
        private readonly RafisAlgoPackingService _rafisAlgoPacker = new();

        /// <summary>
        /// V022 NEW: normalized shape all 3 packing services (MasterRowBand,
        /// FreeRect, RafisAlgo) are adapted into below, so the rest of
        /// Pack() — sheet-space conversion, row-tail fallback, overlap
        /// check, overflow relocation — reads one shape regardless of
        /// which of the 7 strategies ran.
        /// </summary>
        private readonly struct NormalizedPlacement
        {
            public readonly ViewOnSheetItem Item;
            public readonly double X, Y; // top-based intermediate space, same convention as all 3 services
            public NormalizedPlacement(ViewOnSheetItem item, double x, double y) { Item = item; X = x; Y = y; }
        }
        private sealed class NormalizedResult
        {
            public List<NormalizedPlacement> Placed { get; set; } = new();
            public List<ViewOnSheetItem> Unplaced { get; set; } = new();
            public double TotalHeightFeet { get; set; }
        }

        // ── Small shared conversions, used throughout Pack() — extracted
        // since "item.WidthMm/HeightMm * MmToFeet" was repeated inline at
        // every call site (6+ times) with no single source of truth.
        private static double WFeet(ViewOnSheetItem item) => item.WidthMm * MmToFeet;
        private static double HFeet(ViewOnSheetItem item) => item.HeightMm * MmToFeet;

        /// <summary>
        /// Axis-aligned left/right/top/bottom bounds of a placement's
        /// footprint, in sheet-space feet. Extracted from the overlap-check
        /// block below, where this exact 4-line "center + half-size ->
        /// bounds" computation was duplicated once for the candidate and
        /// once (inline, in a lambda) for each already-accepted item.
        /// </summary>
        private static (double left, double right, double top, double bottom) GetBounds(PackedViewPlacement p)
        {
            double halfW = WFeet(p.Item) / 2.0;
            double halfH = HFeet(p.Item) / 2.0;
            return (p.NewCenter.X - halfW, p.NewCenter.X + halfW, p.NewCenter.Y + halfH, p.NewCenter.Y - halfH);
        }

        /// <summary>
        /// V024 CHANGE: adds the leftover-item fallback controls —
        /// <paramref name="leftoverGroupMode"/> picks row-based (Y-proximity,
        /// the sole pre-V024 behavior) or column-based (X-proximity)
        /// grouping for whatever the fill strategy above left unplaced;
        /// <paramref name="columnToleranceMm"/> and
        /// <paramref name="withinColumnAlign"/> only matter in Column mode,
        /// mirroring rowToleranceMm/rowAlignment's role in Row mode.
        ///
        /// V026 CHANGE: adds <paramref name="enablePriorityGroups"/> /
        /// <paramref name="priorityOrder"/> — when on, `items` is split into
        /// ordered ViewType groups (BuildPriorityGroups) and each group is
        /// packed via PackGroup into its own shrinking sub-region, top group
        /// first, so every view of the 1st-ranked group ends up above every
        /// view of the 2nd-ranked group, and so on. When off (or the order
        /// is empty — the pre-V026 default), BuildPriorityGroups returns
        /// `items` as a single group and this method's flow is
        /// byte-identical to every version before V026 (PackGroup gets
        /// called exactly once, against the full region).
        /// </summary>
        public List<PackedViewPlacement> Pack(
            List<ViewOnSheetItem> items,
            PlaceableRegion region,
            GapSettings gapSettings,
            double rowToleranceMm,
            RowAlignment rowAlignment,
            BlockAlignmentH blockH,
            BlockAlignmentV blockV,
            RowFillStrategy fillStrategy,
            LeftoverGroupMode leftoverGroupMode,
            double columnToleranceMm,
            BlockAlignmentH withinColumnAlign,
            bool enablePriorityGroups,
            IReadOnlyList<ViewType>? priorityOrder,
            AlignDistributeSettings? alignDistribute = null,
            List<PackNote>? notes = null)
        {
            var results = new List<PackedViewPlacement>();
            if (items.Count == 0)
                return results;

            double toleranceFeet = rowToleranceMm * MmToFeet;
            double columnToleranceFeet = columnToleranceMm * MmToFeet;
            double hGapFeet = gapSettings.GlobalHorizontalGapMm * MmToFeet;
            double vGapFeet = gapSettings.GlobalVerticalGapMm * MmToFeet;

            var largeRect = region.LargeRect;
            double containerWidthFeet = largeRect.Width;

            // V026 NEW: pack each priority group in turn, into a sub-region
            // that starts right below wherever the previous group's lowest
            // placement ended (full width, remaining height). Every group's
            // items — including whatever its own fill strategy couldn't
            // place, laid out by LayOutLeftoversByRow/Column inside
            // PackGroup — flow into `flat` before the shared tail below
            // (block alignment, region-fit check, overlap check, overflow
            // relocation) runs ONCE across the combined result, exactly as
            // it always has for a single pool.
            var groups = BuildPriorityGroups(items, enablePriorityGroups, priorityOrder);

            var flat = new List<PackedViewPlacement>();
            double cursorTopY = largeRect.Max.Y;

            foreach (var group in groups)
            {
                if (group.Count == 0)
                    continue;

                var groupRegion = new PlaceableRegion(region.Mode,
                    new RectFeet(largeRect.Min, new XYZ(largeRect.Max.X, cursorTopY, 0)),
                    region.DisplayText);

                var groupPlacements = PackGroup(group, groupRegion, hGapFeet, vGapFeet,
                    toleranceFeet, rowAlignment, fillStrategy, leftoverGroupMode,
                    columnToleranceFeet, withinColumnAlign);

                flat.AddRange(groupPlacements);

                // Next group starts below this group's lowest edge (main
                // strategy placements AND its own leftover band, whichever
                // reaches further down) — mirrors how LayOutLeftoversByRow's
                // cursorY already continues below TotalHeightFeet today.
                cursorTopY = groupPlacements.Count > 0
                    ? groupPlacements.Min(p => p.NewCenter.Y - HFeet(p.Item) / 2.0) - vGapFeet
                    : cursorTopY;
            }

            if (flat.Count == 0)
                return results;

            // Step 4: whole-block alignment. NOTE: in the UI, blockH/blockV
            // are now surfaced as "Column Align" per explicit request — this
            // still reuses the existing BlockAlignmentH/V mechanism
            // internally (per confirmed design), only the UI label changed.
            // (largeRect already declared above — reused here, not
            // redeclared, to avoid a duplicate-local compile error.)

            double blockMinX = flat.Min(p => p.NewCenter.X - WFeet(p.Item) / 2.0);
            double blockMaxX = flat.Max(p => p.NewCenter.X + WFeet(p.Item) / 2.0);
            double blockMinY = flat.Min(p => p.NewCenter.Y - HFeet(p.Item) / 2.0);
            double blockMaxY = flat.Max(p => p.NewCenter.Y + HFeet(p.Item) / 2.0);

            double blockWidth = blockMaxX - blockMinX;
            double blockHeight = blockMaxY - blockMinY;
            double usableWidth = largeRect.Width;
            double usableHeight = largeRect.Height;

            double shiftX = blockH switch
            {
                BlockAlignmentH.Left => largeRect.Min.X - blockMinX,
                BlockAlignmentH.Right => largeRect.Max.X - blockMaxX,
                _ => (largeRect.Min.X + usableWidth / 2.0) - (blockMinX + blockWidth / 2.0)
            };

            double shiftY = blockV switch
            {
                BlockAlignmentV.Top => largeRect.Max.Y - blockMaxY,
                BlockAlignmentV.Bottom => largeRect.Min.Y - blockMinY,
                _ => (largeRect.Min.Y + usableHeight / 2.0) - (blockMinY + blockHeight / 2.0)
            };

            double shiftedTopY = blockMaxY + shiftY;
            if (shiftedTopY > largeRect.Max.Y)
                shiftY -= (shiftedTopY - largeRect.Max.Y);

            foreach (var placement in flat)
            {
                var shifted = new XYZ(
                    placement.NewCenter.X + shiftX,
                    placement.NewCenter.Y + shiftY,
                    0);

                double halfW = WFeet(placement.Item) / 2.0;
                double halfH = HFeet(placement.Item) / 2.0;

                double itemBandTop = shifted.Y + halfH;
                double itemBandBottom = shifted.Y - halfH;
                var (fitMinX, fitMaxX, fitRect) = region.GetUsableXRangeAtY(itemBandTop, itemBandBottom);

                bool fits = (shifted.X - halfW) >= fitMinX - 1e-6
                            && (shifted.X + halfW) <= fitMaxX + 1e-6
                            && (shifted.Y - halfH) >= fitRect.Min.Y - 1e-6
                            && (shifted.Y + halfH) <= fitRect.Max.Y + 1e-6;

                results.Add(new PackedViewPlacement
                {
                    Item = placement.Item,
                    NewCenter = shifted,
                    Fits = fits
                });
            }

            // ── V013 NEW: pairwise overlap detection ────────────────────
            // The region fit-check above only verifies each view's box sits
            // within the sheet's usable area — it never checked whether two
            // views' boxes overlap EACH OTHER. This is the actual bug
            // behind views showing up on top of one another with "0
            // overflow" reported: every view could individually be inside
            // the usable region while still colliding with a neighbor.
            //
            // Resolution order (flagged, confirmed): the FIRST view in
            // `results` order (i.e. the row-packing/shelf placement
            // sequence already computed above) that occupies a given spot
            // wins and keeps Fits=true; any LATER view whose box overlaps
            // an already-accepted box gets Fits=false. This is O(n^2) —
            // fine for realistic per-sheet view counts (tens, not
            // thousands) — checked only among views that already passed
            // the region fit-check, since a view already flagged Overflow
            // for being outside the region doesn't need an overlap check.
            var accepted = new List<PackedViewPlacement>();
            foreach (var candidate in results)
            {
                if (!candidate.Fits)
                    continue; // already failed the region check — leave as Overflow, don't bother checking overlap

                var (left, right, top, bottom) = GetBounds(candidate);

                bool overlapsAccepted = accepted.Any(a =>
                {
                    var (aLeft, aRight, aTop, aBottom) = GetBounds(a);

                    // Standard axis-aligned rectangle overlap test: two
                    // rects DON'T overlap if one is entirely to the left,
                    // right, above, or below the other. A tiny tolerance
                    // (1e-6 ft ≈ 0.0003mm) avoids flagging boxes that are
                    // merely touching edge-to-edge (e.g. zero-gap packing)
                    // as a false-positive overlap.
                    bool separated = right <= aLeft + 1e-6
                                      || left >= aRight - 1e-6
                                      || bottom >= aTop - 1e-6
                                      || top <= aBottom + 1e-6;
                    return !separated;
                });

                if (overlapsAccepted)
                {
                    candidate.Fits = false;
                }
                else
                {
                    accepted.Add(candidate);
                }
            }

            // NOTE: V014 — the shelf-overflow-grouping block that used to
            // run here (dragging a Tall/Wide anchor's shelved items into
            // Overflow together when the anchor itself didn't fit) has been
            // REMOVED along with the anchor system. Every item's Fits value
            // now comes purely from the region-fit + overlap checks above —
            // there's no "block" concept left to group failures by.

            // ── V014 NEW: actually relocate overflow items below the
            // titleblock's usable area. Previously, an item marked
            // Fits=false simply kept whatever NewCenter the packer had
            // already computed for it (usually still WITHIN the usable
            // region's bounds, or overlapping another view) — the log
            // message claimed "placed below the sheet" but nothing moved
            // it there. This repacks every overflow item into simple rows
            // starting at the region's bottom edge (largeRect.Min.Y) and
            // growing further downward, left-to-right, using the same
            // gap values as everywhere else. This area is intentionally
            // unbounded (nothing below the titleblock constrains it), so
            // these items stay Fits=false (still Overflow — they didn't
            // fit ON the sheet) but now sit at real, non-overlapping
            // coordinates a user can actually find and deal with.
            var overflowItems = results.Where(r => !r.Fits).Select(r => r.Item).ToList();
            if (overflowItems.Count > 0)
            {
                double overflowCursorY = largeRect.Min.Y - vGapFeet;

                // Pack tallest-first per row, matching this file's general
                // convention elsewhere (row height = tallest item in the
                // row); simple left-to-right fill, wrapping to a new row
                // once the row would exceed the titleblock's own width so
                // the overflow group stays visually aligned under it.
                var sortedOverflow = overflowItems.OrderByDescending(i => i.HeightMm).ToList();
                var overflowRows = new List<List<ViewOnSheetItem>>();
                var currentOverflowRow = new List<ViewOnSheetItem>();
                double currentRowWidthFeet = 0.0;

                foreach (var item in sortedOverflow)
                {
                    double wFeet = WFeet(item);
                    double advance = currentOverflowRow.Count > 0 ? wFeet + hGapFeet : wFeet;

                    if (currentOverflowRow.Count > 0 && currentRowWidthFeet + advance > containerWidthFeet + 1e-6)
                    {
                        overflowRows.Add(currentOverflowRow);
                        currentOverflowRow = new List<ViewOnSheetItem>();
                        currentRowWidthFeet = 0.0;
                        advance = wFeet;
                    }

                    currentOverflowRow.Add(item);
                    currentRowWidthFeet += advance;
                }
                if (currentOverflowRow.Count > 0)
                    overflowRows.Add(currentOverflowRow);

                foreach (var row in overflowRows)
                {
                    double rowHeightFeet = row.Max(i => i.HeightMm) * MmToFeet;
                    double rowTopY = overflowCursorY;
                    double rowBottomY = rowTopY - rowHeightFeet;
                    double cursorX = largeRect.Min.X;

                    foreach (var item in row)
                    {
                        double wFeet = WFeet(item);
                        double hFeet = HFeet(item);

                        // Bottom-aligned within its overflow row, matching
                        // this service's general bottom-alignment
                        // convention (RowAlignment default, MasterRowBand
                        // strategies) — keeps shorter overflow items flush
                        // with taller neighbors instead of floating.
                        double itemBottom = rowBottomY;
                        double itemTop = itemBottom + hFeet;
                        var center = new XYZ(cursorX + wFeet / 2.0, (itemTop + itemBottom) / 2.0, 0);

                        var existing = results.First(r => r.Item.ViewportId == item.ViewportId);
                        existing.NewCenter = center;
                        // existing.Fits stays false — this is still Overflow,
                        // just relocated to a real, findable spot below the sheet.

                        cursorX += wFeet + hGapFeet;
                    }

                    overflowCursorY = rowBottomY - vGapFeet;
                }
            }

            // V029 NEW: Align & Distribute — rebuilds the views that fit as aligned
            // rows / columns / a table inside the usable area. Off (the default)
            // skips this, so the result above is returned exactly as in V028.
            if (alignDistribute != null && alignDistribute.IsActive)
                ApplyAlignDistribute(results, largeRect, alignDistribute, hGapFeet, vGapFeet,
                    toleranceFeet, columnToleranceFeet, blockH, blockV, notes);

            return results;
        }

        /// <summary>
        /// V029 NEW: moves the views that fit onto shared row / column lines and spreads
        /// them across the usable area (see <see cref="AlignDistributeLayout"/>). Overflow
        /// views keep their place below the sheet. When the aligned layout does not fit,
        /// or would overlap, nothing is moved and a warning note is added — the packed
        /// layout is kept (owner's choice: fall back and warn, never push a view to overflow).
        /// </summary>
        private static void ApplyAlignDistribute(
            List<PackedViewPlacement> results, RectFeet area, AlignDistributeSettings settings,
            double hGapFeet, double vGapFeet, double rowToleranceFeet, double columnToleranceFeet,
            BlockAlignmentH blockH, BlockAlignmentV blockV, List<PackNote>? notes)
        {
            var fitting = results.Where(r => r.Fits).ToList();
            if (fitting.Count == 0)
                return;

            var boxes = fitting
                .Select((p, i) => new LayoutBox(i, p.NewCenter.X, p.NewCenter.Y, WFeet(p.Item), HFeet(p.Item)))
                .ToList();

            var layout = AlignDistributeLayout.Arrange(boxes,
                area.Min.X, area.Min.Y, area.Max.X, area.Max.Y, settings,
                hGapFeet, vGapFeet, rowToleranceFeet, columnToleranceFeet, blockH, blockV);

            if (!layout.Applied)
            {
                string side = layout.FailReason == AlignFailReason.WidthExceeded ? "width" : "height";
                notes?.Add(new PackNote(true,
                    $"Align & Distribute skipped — the aligned layout needs {layout.Needed / MmToFeet:0} mm of {side}, " +
                    $"the usable area has {layout.Available / MmToFeet:0} mm. Normal packing kept."));
                return;
            }

            // Last check before anything moves: every view inside the area, no two overlapping.
            var moved = boxes.Select(b => (b, c: layout.Centers[b.Key])).ToList();
            bool safe = moved.All(m =>
                m.c.X - m.b.Width / 2.0 >= area.Min.X - 1e-6 && m.c.X + m.b.Width / 2.0 <= area.Max.X + 1e-6 &&
                m.c.Y - m.b.Height / 2.0 >= area.Min.Y - 1e-6 && m.c.Y + m.b.Height / 2.0 <= area.Max.Y + 1e-6);
            for (int i = 0; safe && i < moved.Count; i++)
            {
                for (int j = i + 1; j < moved.Count; j++)
                {
                    bool apart = Math.Abs(moved[i].c.X - moved[j].c.X) >= (moved[i].b.Width + moved[j].b.Width) / 2.0 - 1e-6
                              || Math.Abs(moved[i].c.Y - moved[j].c.Y) >= (moved[i].b.Height + moved[j].b.Height) / 2.0 - 1e-6;
                    if (!apart)
                    {
                        safe = false;
                        break;
                    }
                }
            }

            if (!safe)
            {
                notes?.Add(new PackNote(true, "Align & Distribute skipped — the aligned layout would overlap or leave the usable area. Normal packing kept."));
                return;
            }

            foreach (var (b, c) in moved)
                fitting[b.Key].NewCenter = new XYZ(c.X, c.Y, 0);

            notes?.Add(new PackNote(false,
                $"Align & Distribute applied — {layout.Rows} row(s) × {layout.Columns} column(s), {fitting.Count} view(s)."));
        }

        /// <summary>
        /// V026 NEW: runs the main fill strategy + leftover fallback for ONE
        /// priority group against ITS OWN region (a full-width slice of the
        /// sheet's usable area, starting at that group's own top edge — see
        /// the Pack() loop that builds groupRegion per group). This is
        /// exactly the pre-V026 body of Pack() from the fill-strategy switch
        /// through the leftover layout, extracted unchanged so a single
        /// no-priority-grouping call (Pack()'s groups == [items] case) stays
        /// byte-identical to every version before V026. Returns every
        /// group item's placement — some may still land past `region`'s own
        /// bottom edge (that's fine; the FINAL region-fit check in Pack()
        /// uses the outer, full-sheet region, not this group's slice).
        /// </summary>
        private List<PackedViewPlacement> PackGroup(
            List<ViewOnSheetItem> items,
            PlaceableRegion region,
            double hGapFeet,
            double vGapFeet,
            double toleranceFeet,
            RowAlignment rowAlignment,
            RowFillStrategy fillStrategy,
            LeftoverGroupMode leftoverGroupMode,
            double columnToleranceFeet,
            BlockAlignmentH withinColumnAlign)
        {
            var largeRect = region.LargeRect;
            double containerWidthFeet = largeRect.Width;
            double containerHeightMaxFeet = largeRect.Height;

            // V014: MasterRowBandPackingService's single `gap` parameter uses
            // the Horizontal global gap for both axes, per explicit request
            // ("keep separate H/V gap fields, just feed the same value to
            // both internally") — the spec itself only supports one uniform
            // gap value, so H is the chosen source of truth here.
            //
            // V022 NEW: MaxFill/Skyline/SkylineWasteMap and RafisAlgo run
            // through separate services (see class remarks) but are
            // normalized into the same shape below — FreeRectPackingService
            // and RafisAlgoPackingService both take H and V gap separately
            // (unlike MasterRowBandPackingService's single uniform `gap`),
            // since neither one's spec has that one-gap limitation.
            var masterResult = new NormalizedResult();
            switch (fillStrategy)
            {
                case RowFillStrategy.Shelf:
                case RowFillStrategy.Guillotine:
                case RowFillStrategy.MaxRects:
                    {
                        var r = _masterRowPacker.Pack(items, containerWidthFeet, containerHeightMaxFeet, hGapFeet, fillStrategy);
                        masterResult.Placed = r.Placed.Select(p => new NormalizedPlacement(p.Item, p.X, p.Y)).ToList();
                        masterResult.Unplaced = r.Unplaced;
                        masterResult.TotalHeightFeet = r.TotalHeightFeet;
                        break;
                    }
                case RowFillStrategy.MaxFill:
                case RowFillStrategy.Skyline:
                case RowFillStrategy.SkylineWasteMap:
                    {
                        var r = _freeRectPacker.Pack(items, containerWidthFeet, containerHeightMaxFeet, hGapFeet, vGapFeet, fillStrategy);
                        masterResult.Placed = r.Placed.Select(p => new NormalizedPlacement(p.Item, p.X, p.Y)).ToList();
                        masterResult.Unplaced = r.Unplaced;
                        masterResult.TotalHeightFeet = r.TotalHeightFeet;
                        break;
                    }
                case RowFillStrategy.RafisAlgo:
                    {
                        var r = _rafisAlgoPacker.Pack(items, containerWidthFeet, containerHeightMaxFeet, hGapFeet, vGapFeet);
                        masterResult.Placed = r.Placed.Select(p => new NormalizedPlacement(p.Item, p.X, p.Y)).ToList();
                        masterResult.Unplaced = r.Unplaced;
                        masterResult.TotalHeightFeet = r.TotalHeightFeet;
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(fillStrategy));
            }

            // Convert the service's top-based (row 0 at Y=0, growing down)
            // intermediate space into sheet-space centers. Row-space Y=0
            // maps to this group's region top edge (largeRect.Max.Y); Y
            // grows downward in row-space, so sheet Y = largeRect.Max.Y - rowY.
            var masterPlacements = new List<PackedViewPlacement>();
            foreach (var p in masterResult.Placed)
            {
                double wFeet = WFeet(p.Item);
                double hFeet = HFeet(p.Item);

                double sheetTopY = largeRect.Max.Y - p.Y;
                double sheetBottomY = sheetTopY - hFeet;
                double centerY = (sheetTopY + sheetBottomY) / 2.0;

                double sheetLeftX = largeRect.Min.X + p.X;
                double centerX = sheetLeftX + wFeet / 2.0;

                masterPlacements.Add(new PackedViewPlacement
                {
                    Item = p.Item,
                    NewCenter = new XYZ(centerX, centerY, 0),
                    Fits = true
                });
            }

            var remainingItems = masterResult.Unplaced;

            var laidOutBands = leftoverGroupMode == LeftoverGroupMode.Column
                ? LayOutLeftoversByColumn(remainingItems, region, largeRect, masterPlacements,
                    columnToleranceFeet, withinColumnAlign, hGapFeet, vGapFeet)
                : LayOutLeftoversByRow(remainingItems, region, largeRect, masterResult,
                    toleranceFeet, rowAlignment, hGapFeet, vGapFeet);

            return masterPlacements.Concat(laidOutBands.SelectMany(r => r)).ToList();
        }

        /// <summary>
        /// V026 NEW: splits `items` into ordered priority groups per
        /// <paramref name="priorityOrder"/> — each listed ViewType becomes
        /// its own group, in the ranked order the user configured, and every
        /// group's items place before any item in the next group (see the
        /// Pack() loop). Items whose ViewType isn't listed fall into one
        /// trailing "unranked" group, in their original relative order —
        /// so a view type that shows up on a sheet but was never ranked
        /// still gets placed, just last. Each group preserves `items`'
        /// original relative order (stable LINQ Where over the source list).
        ///
        /// When <paramref name="enabled"/> is false or
        /// <paramref name="priorityOrder"/> is null/empty (the pre-V026
        /// default), returns `items` as a single group — Pack()'s loop then
        /// runs exactly once, over the full region, identical to every
        /// version before V026.
        /// </summary>
        private static List<List<ViewOnSheetItem>> BuildPriorityGroups(
            List<ViewOnSheetItem> items, bool enabled, IReadOnlyList<ViewType>? priorityOrder)
        {
            if (!enabled || priorityOrder == null || priorityOrder.Count == 0)
                return new List<List<ViewOnSheetItem>> { items };

            var groups = new List<List<ViewOnSheetItem>>();
            var claimed = new HashSet<ElementId>();

            foreach (var viewType in priorityOrder)
            {
                var group = items.Where(i => i.ViewType == viewType && !claimed.Contains(i.ViewportId)).ToList();
                if (group.Count == 0)
                    continue;

                foreach (var i in group)
                    claimed.Add(i.ViewportId);
                groups.Add(group);
            }

            var unranked = items.Where(i => !claimed.Contains(i.ViewportId)).ToList();
            if (unranked.Count > 0)
                groups.Add(unranked);

            return groups;
        }

        /// <summary>
        /// The original (pre-V024) leftover-item fallback: groups
        /// unplaced items into horizontal bands by Y-proximity (Row
        /// Tolerance), each band filled right-to-left, with shorter items
        /// aligned per rowAlignment against the band's tallest item. Bands
        /// start right below wherever the main fill strategy's block ended
        /// (or the region's top edge, if it placed nothing).
        /// </summary>
        private List<List<PackedViewPlacement>> LayOutLeftoversByRow(
            List<ViewOnSheetItem> remainingItems,
            PlaceableRegion region,
            RectFeet largeRect,
            NormalizedResult masterResult,
            double toleranceFeet,
            RowAlignment rowAlignment,
            double hGapFeet,
            double vGapFeet)
        {
            var rows = GroupIntoRows(remainingItems, toleranceFeet);
            foreach (var row in rows)
                row.Sort((a, b) => b.CurrentCenter.X.CompareTo(a.CurrentCenter.X));

            var laidOutRows = new List<List<PackedViewPlacement>>();
            double cursorY = masterResult.Placed.Count > 0
                ? (largeRect.Max.Y - masterResult.TotalHeightFeet)
                : region.GetOverallBounds().Max.Y;

            foreach (var row in rows)
            {
                double rowHeightFeet = row.Max(i => i.HeightMm) * MmToFeet;
                double bandTop = cursorY;
                double bandBottom = cursorY - rowHeightFeet;

                var (rangeMinX, rangeMaxX, usedRect) = region.GetUsableXRangeAtY(bandTop, bandBottom);
                double cursorX = rangeMaxX;

                var rowResults = new List<PackedViewPlacement>();

                foreach (var item in row)
                {
                    double wFeet = WFeet(item);
                    double hFeet = HFeet(item);

                    double right = cursorX;
                    double left = right - wFeet;

                    double top = rowAlignment switch
                    {
                        RowAlignment.Top => cursorY,
                        RowAlignment.Bottom => cursorY - (rowHeightFeet - hFeet),
                        _ => cursorY - (rowHeightFeet - hFeet) / 2.0
                    };
                    double bottom = top - hFeet;

                    var center = new XYZ((left + right) / 2.0, (top + bottom) / 2.0, 0);

                    rowResults.Add(new PackedViewPlacement { Item = item, NewCenter = center, Fits = true });

                    cursorX = left - hGapFeet;
                }

                laidOutRows.Add(rowResults);

                cursorY -= rowHeightFeet + vGapFeet;
            }

            return laidOutRows;
        }

        /// <summary>
        /// V024 NEW: the column-mode mirror of LayOutLeftoversByRow — groups
        /// unplaced items into vertical bands by X-proximity (Column
        /// Tolerance), each band filled top-to-bottom, with narrower items
        /// aligned per withinColumnAlign against the band's widest item.
        /// Bands start immediately right of the main fill strategy's placed
        /// block (its rightmost occupied edge — the main strategy has no
        /// single "total width" the way it has TotalHeightFeet, since it's a
        /// row/band packer that grows downward, so the widest placed item's
        /// right edge is used as the safe starting point instead), or the
        /// region's left edge if it placed nothing. Each band spans the
        /// region's full usable height, top to bottom — mirroring how a row
        /// band spans the region's full usable width regardless of what the
        /// main strategy actually used.
        /// </summary>
        private List<List<PackedViewPlacement>> LayOutLeftoversByColumn(
            List<ViewOnSheetItem> remainingItems,
            PlaceableRegion region,
            RectFeet largeRect,
            List<PackedViewPlacement> masterPlacements,
            double toleranceFeet,
            BlockAlignmentH withinColumnAlign,
            double hGapFeet,
            double vGapFeet)
        {
            var columns = GroupIntoColumns(remainingItems, toleranceFeet);
            foreach (var column in columns)
                column.Sort((a, b) => b.CurrentCenter.Y.CompareTo(a.CurrentCenter.Y));

            var laidOutColumns = new List<List<PackedViewPlacement>>();
            double cursorX = masterPlacements.Count > 0
                ? masterPlacements.Max(p => p.NewCenter.X + WFeet(p.Item) / 2.0) + hGapFeet
                : region.GetOverallBounds().Min.X;

            var overallBounds = region.GetOverallBounds();

            foreach (var column in columns)
            {
                double columnWidthFeet = column.Max(i => i.WidthMm) * MmToFeet;
                double bandLeft = cursorX;
                double bandRight = cursorX + columnWidthFeet;

                double rangeMaxY = overallBounds.Max.Y;
                double cursorY = rangeMaxY;

                var columnResults = new List<PackedViewPlacement>();

                foreach (var item in column)
                {
                    double wFeet = WFeet(item);
                    double hFeet = HFeet(item);

                    double top = cursorY;
                    double bottom = top - hFeet;

                    double left = withinColumnAlign switch
                    {
                        BlockAlignmentH.Left => bandLeft,
                        BlockAlignmentH.Right => bandRight - wFeet,
                        _ => bandLeft + (columnWidthFeet - wFeet) / 2.0
                    };
                    double right = left + wFeet;

                    var center = new XYZ((left + right) / 2.0, (top + bottom) / 2.0, 0);

                    columnResults.Add(new PackedViewPlacement { Item = item, NewCenter = center, Fits = true });

                    cursorY = bottom - vGapFeet;
                }

                laidOutColumns.Add(columnResults);

                cursorX = bandRight + hGapFeet;
            }

            return laidOutColumns;
        }

        private List<List<ViewOnSheetItem>> GroupIntoRows(List<ViewOnSheetItem> items, double toleranceFeet)
        {
            if (items.Count == 0)
                return new List<List<ViewOnSheetItem>>();

            var sorted = items.OrderByDescending(i => i.CurrentCenter.Y).ToList();

            var rows = new List<List<ViewOnSheetItem>>();
            var currentRow = new List<ViewOnSheetItem>();
            double? currentRowY = null;

            foreach (var item in sorted)
            {
                if (currentRowY == null || Math.Abs(item.CurrentCenter.Y - currentRowY.Value) <= toleranceFeet)
                {
                    currentRow.Add(item);
                    currentRowY ??= item.CurrentCenter.Y;
                }
                else
                {
                    rows.Add(currentRow);
                    currentRow = new List<ViewOnSheetItem> { item };
                    currentRowY = item.CurrentCenter.Y;
                }
            }

            if (currentRow.Count > 0)
                rows.Add(currentRow);

            return rows;
        }

        /// <summary>V024 NEW: the column-mode mirror of GroupIntoRows — groups by X-proximity instead of Y.</summary>
        private List<List<ViewOnSheetItem>> GroupIntoColumns(List<ViewOnSheetItem> items, double toleranceFeet)
        {
            if (items.Count == 0)
                return new List<List<ViewOnSheetItem>>();

            var sorted = items.OrderBy(i => i.CurrentCenter.X).ToList();

            var columns = new List<List<ViewOnSheetItem>>();
            var currentColumn = new List<ViewOnSheetItem>();
            double? currentColumnX = null;

            foreach (var item in sorted)
            {
                if (currentColumnX == null || Math.Abs(item.CurrentCenter.X - currentColumnX.Value) <= toleranceFeet)
                {
                    currentColumn.Add(item);
                    currentColumnX ??= item.CurrentCenter.X;
                }
                else
                {
                    columns.Add(currentColumn);
                    currentColumn = new List<ViewOnSheetItem> { item };
                    currentColumnX = item.CurrentCenter.X;
                }
            }

            if (currentColumn.Count > 0)
                columns.Add(currentColumn);

            return columns;
        }
    }
}
