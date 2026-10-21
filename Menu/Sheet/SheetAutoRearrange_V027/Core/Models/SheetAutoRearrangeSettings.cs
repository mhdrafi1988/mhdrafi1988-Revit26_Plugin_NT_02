using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V027.Core.Models
{
    /// <summary>
    /// Everything persisted to %AppData%\Revit26_Plugin_Revised\SheetAutoRearrange\settings.json,
    /// per suite convention. Loaded on window open, saved on window close and
    /// after each Run.
    ///
    /// V012 CHANGE: adds ColumnToleranceMm and WithinColumnAlign — NEW
    /// FIELDS ONLY, PERSISTED BUT NOT YET WIRED INTO ANY PACKING LOGIC.
    /// A true "Column Tolerance" mirroring Row Tolerance would require a
    /// second, parallel column-based packing algorithm (grouping views into
    /// vertical columns by X-proximity, mirroring how Row Tolerance groups
    /// into horizontal rows by Y-proximity) — that's substantial new
    /// algorithm work that was explicitly NOT confirmed as in-scope for this
    /// version. These two fields exist in the UI and persist correctly, but
    /// currently have NO EFFECT on placement. Flagged clearly — do not
    /// assume they do anything yet.
    ///
    /// V014 CHANGE: TallDetectionSettings/WideDetectionSettings REMOVED.
    /// The Tall/Wide anchor + L-shape shelf system (ShelfPackingService,
    /// ViewSizeClassifierService, ViewSizeCategory, ShelfBlock) is removed
    /// entirely per explicit request, replaced by MasterRowBandPackingService
    /// — a uniform Master Row + height-Band system with three selectable
    /// fill strategies (RowFillStrategy: Shelf/Guillotine/MaxRects). There is
    /// no anchor concept in the new system; every item, regardless of size,
    /// goes through the same Master Row/Band pipeline. Ported from the
    /// user-supplied algorithm spec EXACTLY AS WRITTEN, including a known
    /// row-height plateau behavior (Master Row height = tallest item still
    /// anywhere unplaced, not tallest-in-this-row) — left unfixed per
    /// explicit request ("port as-specified, I want to test it myself").
    ///
    /// V015 CHANGE: SheetOrderPackingService now actually relocates
    /// overflow items (Fits=false) below the titleblock's usable area
    /// instead of leaving them at their originally-computed (possibly
    /// in-bounds/overlapping) coordinates — see that class's remarks for
    /// the relocation logic. Also: MasterRowBandPackingService internals
    /// refactored (StartMasterRow / PackColumnsIntoRow helpers extracted
    /// from previously-duplicated code in PackShelf/PackFreeRect) —
    /// behavior-preserving, verified via parallel Python simulation
    /// producing identical placement counts before/after. No settings
    /// schema changes in V015 — this class is unchanged from V014.
    ///
    /// V021 CHANGE: SheetOrderPackingService internals refactored —
    /// extracted WFeet/HFeet (item.WidthMm/HeightMm * MmToFeet, previously
    /// repeated inline at 8+ call sites) and GetBounds (center + half-size
    /// -> left/right/top/bottom, previously duplicated once for the
    /// overlap-check candidate and once inline in the accepted-items
    /// lambda) as shared private helpers. Behavior-preserving — re-verified
    /// via the same parallel Python simulation, identical placement counts.
    /// No settings schema changes — this class is unchanged from V015.
    ///
    /// V021 CLEANUP (2): Confirmed-dead code removed outright —
    /// RearrangeAlgorithm.cs (unused enum), ReadingOrderPackingService.cs
    /// (unused since V009, per RearrangeEngine's remarks), and
    /// PlaceableRegion.SmallRect/IsLShape/RectFeet.ContainsYBand (vestigial
    /// since V008, dead since V014 — all 3 constructor call sites always
    /// passed null). PlaceableRegion's constructor signature changed
    /// accordingly (smallRect parameter removed) — all 3 call sites in
    /// TitleBlockDetectionService updated to match. ColumnToleranceMm/
    /// WithinColumnAlign intentionally NOT touched — still persisted, still
    /// not wired into packing logic, per explicit instruction to leave
    /// those alone.
    ///
    /// V022 CHANGE: RowFillStrategy grows from 3 to 7 members (MaxFill,
    /// RafisAlgo, Skyline, SkylineWasteMap added — see that enum and the
    /// new FreeRectPackingService/RafisAlgoPackingService). Default changed
    /// Shelf -> MaxFill per explicit spec. System.Text.Json serializes
    /// enums by name by default, so no schema/converter change needed —
    /// any settings.json saved under V021 with RowFillStrategy still set
    /// to one of the original 3 names deserializes unchanged.
    ///
    /// V024 CHANGE: ColumnToleranceMm/WithinColumnAlign are now wired into
    /// SheetOrderPackingService's leftover-item fallback layout (previously
    /// a no-op placeholder, per the V012/V021 remarks above). New
    /// LeftoverGroupMode field selects whether that fallback groups by row
    /// (Y-proximity via RowToleranceMm/RowAlignment, the pre-V024 behavior)
    /// or by column (X-proximity via ColumnToleranceMm/WithinColumnAlign).
    /// Defaults to Row, so existing settings.json files behave identically
    /// until a person explicitly switches the new toggle.
    ///
    /// V026 CHANGE: adds EnablePriorityGroups/PriorityOrder — lets the user
    /// rank ViewTypes (e.g. Sections before Drafting Views) so
    /// SheetOrderPackingService places every view of the 1st-ranked type
    /// before any view of the 2nd-ranked type, and so on. See
    /// SheetOrderPackingService.BuildPriorityGroups. Defaults to disabled/
    /// empty, so upgrading from a prior settings.json is behavior-preserving
    /// (single pool, same as every version before V026).
    /// </summary>
    public class SheetAutoRearrangeSettings
    {
        public GapSettings GapSettings { get; set; } = new();

        /// <summary>V014 NEW, V022 CHANGE — which of the 7 fill strategies to use (3 band-based + 4 free-packing). Default MaxFill per V022 spec (was Shelf).</summary>
        public RowFillStrategy RowFillStrategy { get; set; } = RowFillStrategy.MaxFill;

        public double RowToleranceMm { get; set; } = 50;
        public RowAlignment RowAlignment { get; set; } = RowAlignment.Bottom;

        public double ColumnToleranceMm { get; set; } = 50;
        public BlockAlignmentH WithinColumnAlign { get; set; } = BlockAlignmentH.Right;

        /// <summary>
        /// V024 NEW — which of Row Tolerance/Alignment or Column
        /// Tolerance/Within-Column Align actually drives the leftover-item
        /// fallback layout in SheetOrderPackingService. Defaults to Row so
        /// upgrading from a prior settings.json is behavior-preserving.
        /// </summary>
        public LeftoverGroupMode LeftoverGroupMode { get; set; } = LeftoverGroupMode.Row;

        public BlockAlignmentH ColumnAlignH { get; set; } = BlockAlignmentH.Left;
        public BlockAlignmentV ColumnAlignV { get; set; } = BlockAlignmentV.Top;

        public OverflowHandlingMode OverflowHandlingMode { get; set; } = OverflowHandlingMode.PlaceWhatsPlaceable;

        /// <summary>V026 NEW — when true, ticked views are packed group-by-group in PriorityOrder instead of as one pool. Default false.</summary>
        public bool EnablePriorityGroups { get; set; } = false;

        /// <summary>
        /// V026 NEW — ranked placement order (rank = list index) of
        /// ViewTypes when EnablePriorityGroups is on. A ticked view whose
        /// ViewType isn't listed here places after every listed group, in
        /// its original grid order. Empty by default. Persisted by enum
        /// name (System.Text.Json default) so the file stays readable.
        /// </summary>
        public List<ViewType> PriorityOrder { get; set; } = new();
    }
}
