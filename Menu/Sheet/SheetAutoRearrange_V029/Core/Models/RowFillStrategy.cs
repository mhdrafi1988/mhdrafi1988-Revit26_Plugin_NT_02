namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models
{
    /// <summary>
    /// V014 NEW: which fill algorithm is used to place items within the
    /// placeable region. Replaces the V008-V013 Tall/Wide anchor + L-shape
    /// shelf system entirely — there is no anchor concept in any strategy.
    ///
    /// V022 NEW: 4 strategies added (MaxFill, RafisAlgo, Skyline,
    /// SkylineWasteMap) — see "Combined Packing Strategy Spec" doc. Unlike
    /// Shelf/Guillotine/MaxRects, these do NOT use Master Row/Band
    /// scaffolding (see MasterRowBandPackingService header) — they are
    /// implemented in the new sibling services FreeRectPackingService
    /// (MaxFill, Skyline, SkylineWasteMap) and RafisAlgoPackingService
    /// (RafisAlgo). MaxFill is the new UI default.
    /// </summary>
    public enum RowFillStrategy
    {
        /// <summary>
        /// Band 1 placed left-to-right, Bands 2-4 stacked as same-width
        /// sub-row columns bottom-aligned to the Master Row's bottom edge,
        /// then a gap-fill pass pools any remaining Band 2-4 items into
        /// further columns using the row's full height budget. Simplest,
        /// least space-efficient of the group. Band-based (see
        /// MasterRowBandPackingService).
        /// </summary>
        Shelf,

        /// <summary>
        /// Best-Area-Fit selection against a free-rectangle list, 2-way
        /// (right/below) split per placement. Good balance of simplicity
        /// and packing density. Band-based (see MasterRowBandPackingService).
        /// </summary>
        Guillotine,

        /// <summary>
        /// Best-Short-Side-Fit selection against a free-rectangle list,
        /// up to 4-way split per placement (checked against the consumed
        /// rectangle AND every other free rectangle still in the list).
        /// Densest of the band-based group, especially once item widths
        /// and heights both vary — more bookkeeping than Guillotine.
        /// Band-based (see MasterRowBandPackingService).
        /// </summary>
        MaxRects,

        /// <summary>
        /// V022 NEW. No Master Row/Band scaffolding. Items sorted ascending
        /// by area (smallest first), packed directly into a single
        /// free-rectangle list spanning the whole region via
        /// Best-Short-Side-Fit (ties broken by the candidate leaving the
        /// MAX remaining free area after split), up to 4-way split per
        /// placement. New UI default. See FreeRectPackingService.PackMaxFill.
        /// </summary>
        MaxFill,

        /// <summary>
        /// V022 NEW. No Master Row/Band scaffolding, but retains its own
        /// row/baseline progression: Master Row height = tallest item
        /// still remaining (recomputed per row). Within each row, a
        /// recursive Column -> Sub-Row -> Sub-Column climb: place the
        /// tallest fitting item as a Column anchor (column width = that
        /// item's own width), then climb upward filling Sub-Row bands
        /// within that same column-width budget — each band packs
        /// items side by side, band height = tallest item placed in it —
        /// until nothing more fits or the row's top edge is reached, then
        /// advance to the next Column (fresh full row height) until the
        /// row's width is exhausted. See RafisAlgoPackingService — every
        /// mechanic above (row height source, column width source,
        /// sub-row width scope, side-by-side sub-row packing, recursion
        /// stop condition, next-column start state) confirmed explicitly.
        /// </summary>
        RafisAlgo,

        /// <summary>
        /// V022 NEW. No Master Row/Band scaffolding. Bottom-left skyline
        /// (1D height profile across the region's width) — for each item,
        /// scans all x-positions for the lowest resulting skyline height
        /// under the item's footprint, places there, then raises the
        /// profile. Simpler/faster than MaxFill, slightly less dense (no
        /// free-rect list). See FreeRectPackingService.PackSkyline.
        /// </summary>
        Skyline,

        /// <summary>
        /// V022 NEW. Same as Skyline, plus a waste map of small gap
        /// rectangles left below the profile when neighboring columns
        /// were already taller. Before each item's main skyline scan, the
        /// waste map is checked first for a fit — closes Skyline's main
        /// weakness (small unused pockets under a jagged profile) at the
        /// cost of maintaining the extra rect list. See
        /// FreeRectPackingService.PackSkylineWasteMap.
        /// </summary>
        SkylineWasteMap
    }
}
