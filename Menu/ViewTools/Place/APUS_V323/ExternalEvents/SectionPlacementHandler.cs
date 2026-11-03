// File: SectionPlacementHandler.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.APUS.V323.Models;
using Revit26_Plugin_Revised.APUS.V323.Services;
using Revit26_Plugin_Revised.APUS.V323.Services.PlacingAlgorithms;
using Revit26_Plugin_Revised.APUS.V323.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.APUS.V323.ExternalEvents
{
    /// <summary>
    /// SINGLE SOURCE OF TRUTH for Revit document modification. All document
    /// changes occur within a single TransactionGroup; per-viewport failure
    /// isolation happens via SubTransaction, per Rafi's decision, so one
    /// viewport failure never rolls back the whole sheet.
    ///
    /// Pipeline (replaces V320's SimpleGridPlacementService call):
    ///  1. Validate + compute the (single, reusable) placement area.
    ///  2. Resolve marker points for every candidate section (Pass 0).
    ///  3. Sort into reading order (EvenGapPlacementService.SortByReadingOrder).
    ///  4. Pass 1 (measurement): create each candidate as a temp viewport on a
    ///     scratch sheet, measure via GetBoxOutline, delete the scratch sheet.
    ///  5. Build the sheet-by-sheet plan (BuildPlan) — no per-sheet cap,
    ///     oversized views pulled out as failures.
    ///  6. For each sheet group: create/reuse a sheet (SheetNumberService
    ///     handles "{prefix}-{3-digit}" numbering with auto-skip of taken
    ///     numbers), compute the row layout (ComputeRowLayout), then place
    ///     each final viewport inside its own SubTransaction.
    /// </summary>
    public class SectionPlacementHandler : IExternalEventHandler
    {
        public AutoPlaceSectionsViewModel ViewModel { get; set; }
        public List<SectionItemViewModel> SectionsToPlace { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc?.Document;

            if (doc == null)
            {
                ViewModel?.LogError("No active document.");
                ViewModel?.OnPlacementComplete(false, "No active document.");
                return;
            }

            var selected = SectionsToPlace ?? ViewModel?.Sections?.Where(x => x.IsSelected).ToList()
                ?? new List<SectionItemViewModel>();

            if (!selected.Any())
            {
                ViewModel?.OnPlacementComplete(false, "No sections selected for placement.");
                return;
            }

            if (ViewModel?.SelectedTitleBlock == null)
            {
                ViewModel?.OnPlacementComplete(false, "No title block selected.");
                return;
            }

            var titleBlock = doc.GetElement(ViewModel.SelectedTitleBlock.SymbolId) as FamilySymbol;
            if (titleBlock == null)
            {
                ViewModel?.OnPlacementComplete(false, "Selected title block is invalid.");
                return;
            }

            var referenceView = uidoc.ActiveView;
            if (referenceView == null)
            {
                ViewModel?.OnPlacementComplete(false, "No active view for spatial sorting.");
                return;
            }

            if (!ValidateMargins())
            {
                ViewModel?.OnPlacementComplete(false,
                    "Invalid margin settings. Margins must be non-negative.");
                return;
            }

            var placementService = new EvenGapPlacementService((level, msg) => Log(level, msg));

            using var transactionGroup = new TransactionGroup(doc, "APUS V321 - Auto Place Sections");
            try
            {
                transactionGroup.Start();

                // --- Compute the reusable placement area from a scratch sheet ---
                SheetPlacementArea area;
                using (var areaTx = new Transaction(doc, "Compute placement area"))
                {
                    areaTx.Start();
                    var scratchSheetId = CreateScratchSheet(doc, titleBlock);
                    if (scratchSheetId == ElementId.InvalidElementId)
                    {
                        areaTx.RollBack();
                        transactionGroup.RollBack();
                        ViewModel.OnPlacementComplete(false, "Failed to compute placement area.");
                        return;
                    }

                    var scratchSheet = doc.GetElement(scratchSheetId) as ViewSheet;
                    area = SheetLayoutService.Calculate(
                        scratchSheet,
                        ViewModel.LeftMarginMm, ViewModel.RightMarginMm,
                        ViewModel.TopMarginMm, ViewModel.BottomMarginMm);

                    doc.Delete(scratchSheetId);
                    areaTx.Commit();
                }

                // --- Pass 0: resolve marker points for each candidate ---
                var candidates = new List<PlacementCandidate>();
                foreach (var section in selected)
                {
                    var (point, resolved) = ResolveMarker(section.View, ViewModel.MarkerSource);
                    candidates.Add(new PlacementCandidate
                    {
                        SectionView = section.View,
                        MarkerPoint = point,
                        MarkerResolved = resolved
                    });
                }

                // --- Sort into reading order ---
                double yTolFt = Helpers.UnitConversionHelper.MmToFeet(ViewModel.YToleranceMm);
                var sorted = placementService.SortByReadingOrder(
                    candidates, referenceView, ViewModel.ReadingDirection,
                    yTolFt, ViewModel.RowTiebreak, ViewModel.SortFallback);

                // --- Pass 1: measure real footprints via temp viewports on a scratch sheet ---
                using (var measureTx = new Transaction(doc, "Measure section footprints"))
                {
                    measureTx.Start();
                    var scratchSheetId = CreateScratchSheet(doc, titleBlock);
                    if (scratchSheetId == ElementId.InvalidElementId)
                    {
                        measureTx.RollBack();
                        transactionGroup.RollBack();
                        ViewModel.OnPlacementComplete(false, "Failed to create scratch sheet for measurement.");
                        return;
                    }

                    var scratchSheet = doc.GetElement(scratchSheetId) as ViewSheet;

                    foreach (var c in sorted)
                    {
                        try
                        {
                            var tempVp = Viewport.Create(doc, scratchSheet.Id, c.SectionView.Id,
                                new XYZ(area.Origin.X + 5, area.Origin.Y - 5, 0));
                            c.Footprint = ViewportMeasurementService.Measure(tempVp);
                            c.IsMeasurable = true;
                        }
                        catch (Exception ex)
                        {
                            Log(LogLevel.Warning, $"{c.SectionView.Name} could not be measured: {ex.Message}");
                            c.Footprint = new SectionFootprint(0.05, 0.05);
                            c.IsMeasurable = false;
                        }
                    }

                    doc.Delete(scratchSheetId);
                    measureTx.Commit();
                }

                double hGapMinFt = Helpers.UnitConversionHelper.MmToFeet(ViewModel.HorizontalGapMm);
                double vGapFt = Helpers.UnitConversionHelper.MmToFeet(ViewModel.VerticalGapMm);

                var plan = placementService.BuildPlan(sorted, area.Width, area.Height, hGapMinFt, vGapFt);

                if (!ViewModel.PlaceToMultipleSheets && plan.SheetGroups.Count > 1)
                {
                    // Single-sheet mode: only the first sheet's worth is
                    // placed; the remainder is reported as skipped rather
                    // than silently spilling to new sheets.
                    var overflow = plan.SheetGroups.Skip(1)
                        .SelectMany(sheetRows => sheetRows.SelectMany(row => row))
                        .ToList();
                    plan.SheetGroups = plan.SheetGroups.Take(1).ToList();
                    foreach (var c in overflow)
                        Log(LogLevel.Warning, $"{c.SectionView.Name} skipped — multi-sheet mode is off and it didn't fit on the first sheet.");
                    ViewModel.RunMetrics.Skipped += overflow.Count;
                }

                ViewModel.RunMetrics.Skipped += plan.Oversized.Count + plan.Unmeasurable.Count;

                var sheetNumberService = new SheetNumberService(doc);
                var createdSheetNumbers = new HashSet<string>();
                int detailCounter = 0;
                int placedCount = 0;
                int failedCount = 0;

                for (int sheetIndex = 0; sheetIndex < plan.SheetGroups.Count; sheetIndex++)
                {
                    var rows = plan.SheetGroups[sheetIndex];
                    var sheetCandidates = rows.SelectMany(row => row).ToList();

                    // One Transaction per sheet, kept open for the whole sheet
                    // (creation + all its viewports). SubTransaction requires
                    // an open ambient Transaction to nest inside — closing
                    // the sheet-creation Transaction before placing viewports
                    // (as an earlier version of this handler did) left the
                    // SubTransaction with nothing to nest inside, throwing
                    // "A sub-transaction can only be active inside an open
                    // Transaction." This Transaction is only committed once
                    // every viewport on this sheet has been attempted.
                    using var sheetTx = new Transaction(doc, $"APUS V321 - Place sheet {sheetIndex + 1}");
                    sheetTx.Start();

                    ViewSheet sheet;
                    try
                    {
                        var sheetCreator = new SheetCreationService(doc);
                        sheet = sheetCreator.Create(titleBlock, ViewModel.SheetPrefix);
                        createdSheetNumbers.Add(sheet.SheetNumber);
                    }
                    catch (Exception ex)
                    {
                        sheetTx.RollBack();
                        Log(LogLevel.Error, $"Failed to create sheet {sheetIndex + 1}: {ex.Message}");
                        continue;
                    }

                    // Uses the exact rows BuildPlan computed (including its
                    // height-aware row breaks) rather than re-deriving rows
                    // from a flattened list — see EvenGapPlacementService
                    // for why a second, width-only pass could disagree.
                    var results = placementService.ComputeRowLayout(
                        rows, area, hGapMinFt, vGapFt, sheetIndex, () => ++detailCounter);

                    foreach (var result in results)
                    {
                        using var vpTx = new SubTransaction(doc);
                        try
                        {
                            vpTx.Start();
                            var vp = Viewport.Create(doc, sheet.Id, result.SectionView.Id, result.FinalCenter);
                            if (vp == null) throw new InvalidOperationException("Viewport.Create returned null.");

                            var detailParam = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                            detailParam?.Set(result.DetailNumber.ToString());

                            vpTx.Commit();
                            placedCount++;
                            ViewModel.RunMetrics.Placed++;
                        }
                        catch (Exception ex)
                        {
                            vpTx.RollBack();
                            failedCount++;
                            ViewModel.RunMetrics.Failed++;
                            Log(LogLevel.Warning, $"{result.SectionView.Name} failed to place on {sheet.SheetNumber}: {ex.Message}");
                        }

                        ViewModel.LogProgressUpdate(placedCount + failedCount, sorted.Count, result.SectionView.Name);
                    }

                    sheetTx.Commit();

                    double utilization = placementService.CalculateUtilization(sheetCandidates, area);
                    Log(LogLevel.Info, $"Sheet {sheet.SheetNumber}: {sheetCandidates.Count} views placed, utilization {utilization:F0}% (report only).");
                }

                transactionGroup.Assimilate();

                PostPlacementOperations(uidoc, createdSheetNumbers);
                ViewModel.OnPlacementComplete(true,
                    $"{placedCount} sections placed on {createdSheetNumbers.Count} sheet(s), " +
                    $"{failedCount} failed, {plan.Oversized.Count} oversized skipped, " +
                    $"{plan.Unmeasurable.Count} unmeasurable skipped.");
            }
            catch (Exception ex)
            {
                transactionGroup.RollBack();
                ViewModel?.LogError($"Placement failed: {ex.Message}");
                ViewModel?.OnPlacementComplete(false, ex.Message);
            }
        }

        private bool ValidateMargins() =>
            ViewModel.LeftMarginMm >= 0 && ViewModel.RightMarginMm >= 0 &&
            ViewModel.TopMarginMm >= 0 && ViewModel.BottomMarginMm >= 0;

        private ElementId CreateScratchSheet(Document doc, FamilySymbol titleBlock)
        {
            try
            {
                if (!titleBlock.IsActive) titleBlock.Activate();
                var sheet = ViewSheet.Create(doc, titleBlock.Id);
                return sheet.Id;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// Resolves the marker point per the selected MarkerSource. Falls
        /// back gracefully (resolved = false) when the requested source
        /// isn't available on this view, so SortFallback can take over.
        /// </summary>
        private (XYZ point, bool resolved) ResolveMarker(ViewSection view, MarkerSource source)
        {
            try
            {
                switch (source)
                {
                    case MarkerSource.ViewOrigin:
                        return (view.Origin, true);

                    case MarkerSource.CropBoxCenter:
                    case MarkerSource.SectionCurveMidpoint:
                    default:
                        // NOTE: there is no dedicated Revit API to read a
                        // section marker's line directly, so both
                        // SectionCurveMidpoint (default) and CropBoxCenter
                        // resolve to the same crop-box-center point — the
                        // only verified-safe stand-in available (CropBox is
                        // already used elsewhere in this codebase, e.g.
                        // SectionItemViewModel.GetSectionBounds). Flagging
                        // this so it's not mistaken for two genuinely
                        // different marker sources.
                        var box = view.CropBox;
                        if (box == null) return (XYZ.Zero, false);
                        var mid = (box.Min + box.Max) / 2.0;
                        return (box.Transform.OfPoint(mid), true);
                }
            }
            catch
            {
                return (XYZ.Zero, false);
            }
        }

        private void PostPlacementOperations(UIDocument uidoc, HashSet<string> sheetNumbers)
        {
            if (!sheetNumbers.Any() || !ViewModel.OpenSheetsAfterPlacement)
            {
                ViewModel.LogInfo($"{sheetNumbers.Count} sheet(s) created.");
                return;
            }

            var doc = uidoc.Document;
            var sheetIds = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(sheet => sheetNumbers.Contains(sheet.SheetNumber))
                .Select(sheet => sheet.Id)
                .ToList();

            foreach (var sheetId in sheetIds)
            {
                if (doc.GetElement(sheetId) is ViewSheet sheet)
                    uidoc.ActiveView = sheet;
            }

            ViewModel.LogInfo($"{sheetIds.Count} sheet(s) opened.");
            var sheetList = string.Join(", ", sheetNumbers.OrderBy(s => s, StringComparer.Ordinal));
            ViewModel.LogInfo($"Sheets used: {sheetList}");
        }

        private void Log(LogLevel level, string msg)
        {
            switch (level)
            {
                case LogLevel.Warning: ViewModel?.LogWarning(msg); break;
                case LogLevel.Error: ViewModel?.LogError(msg); break;
                case LogLevel.Success: ViewModel?.LogSuccess(msg); break;
                default: ViewModel?.LogInfo(msg); break;
            }
        }

        public string GetName() => "APUS V321 - Auto Place Sections Handler";
    }
}
