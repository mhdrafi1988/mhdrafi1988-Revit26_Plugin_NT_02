using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.AutomatedSectionPlacer.V003.Models;
using Revit26_Plugin_Revised.AutomatedSectionPlacer.V003.Services;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AutomatedSectionPlacer.V003.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Requests this tool's orchestrator handler can execute. Set on the
    /// handler by the ViewModel immediately before calling ExternalEvent.Raise() —
    /// per Revit API threading rules, only one Execute() runs at a time, so
    /// there is no race between setting Request and it being read.
    /// </summary>
    public enum AutomatedSectionPlacerRequest
    {
        LoadViews,
        PlaceViews,
        OpenSheets
    }

    /// <summary>
    /// Single shared orchestrator for all Revit-API-touching actions in this
    /// tool (per our orchestrator-pattern convention: one tool window with
    /// several distinct Revit-side actions uses one handler routed by an
    /// enum, instead of a separate IExternalEventHandler/ExternalEvent pair
    /// per action). Covers:
    ///   - LoadViews:   read all project views + titleblock types (Stage 1)
    ///   - PlaceViews:  create sheets + place viewports (Stage 3)
    ///   - OpenSheets:  set ActiveView to user-selected created sheets (Stage 4)
    /// Stage 2 (packing) is pure calculation and does not touch Revit, so it
    /// has no case here — see ReadingOrderPackingService.
    /// V213: moved from Handlers/ to Infrastructure/ExternalEvents/, matching
    /// the project's vertical-slice folder convention.
    /// </summary>
    public class AutomatedSectionPlacerHandler : IExternalEventHandler
    {
        private readonly UIDocument _uiDoc;

        public AutomatedSectionPlacerRequest Request { get; set; }

        // ---- Inputs (set by ViewModel before Raise()) ----
        /// <summary>The active Plan View to scope section detection to — set by the
        /// Command before construction and never changed afterward (re-scan
        /// always re-queries the same plan view the tool was launched from).</summary>
        public ElementId PlanViewId { get; set; }
        public double MarginTopMm { get; set; }
        public double MarginBottomMm { get; set; }
        public double MarginLeftMm { get; set; }
        public double MarginRightMm { get; set; }
        public List<SheetGroup> SheetsToPlace { get; set; } = new();
        public ElementId? TitleblockFamilySymbolId { get; set; }
        public List<ElementId> SheetIdsToOpen { get; set; } = new();

        // ---- Outputs (read by ViewModel after the handler signals completion) ----
        public List<ViewInfo> LoadedViews { get; private set; } = new();
        public List<TitleblockOption> LoadedTitleblocks { get; private set; } = new();
        /// <summary>Every existing ViewSheet in the project, for the per-group "Target Sheet" dropdown.</summary>
        public List<ExistingSheetOption> LoadedExistingSheets { get; private set; } = new();
        public string PlanViewName { get; private set; } = string.Empty;
        public List<LogEntry> Logs { get; } = new();
        public bool LastRunSucceeded { get; private set; }

        /// <summary>Views successfully placed onto a sheet.</summary>
        public int PlacedCount { get; private set; }

        /// <summary>Views silently skipped (already placed elsewhere, view no longer exists, etc.) — Warning-only, no dialog.</summary>
        public int SkippedCount { get; private set; }

        /// <summary>
        /// V213 fix: previously this counted failed *sheets* (one increment per
        /// sheet whose ViewSheet.Create/setup threw), while the Stage 5 summary
        /// line displayed it as failed *views*, understating true view failures
        /// whenever a failed sheet had more than one queued placement. Now counts
        /// every placement that failed to place, whether the whole sheet failed
        /// or a single viewport failed independently.
        /// </summary>
        public int FailedCount { get; private set; }

        /// <summary>Sheets that failed to create entirely (0 = normal run). Reported separately from FailedCount (views) in the Stage 5 summary.</summary>
        public int FailedSheetCount { get; private set; }

        /// <summary>
        /// Raised after every placement is processed during PlaceViews
        /// (placed, skipped or failed) so the ViewModel can drive the Stage 4
        /// progress bar. Args: processed, total, placed, current view name.
        /// Fired synchronously inside Execute(), i.e. on Revit's UI thread.
        /// </summary>
        public event Action<int, int, int, string>? ProgressChanged;

        private int _processedCount;
        private int _totalToProcess;

        private void ReportProgress(int processedDelta, string current)
        {
            _processedCount += processedDelta;
            ProgressChanged?.Invoke(_processedCount, _totalToProcess, PlacedCount, current);
        }

        /// <summary>Raised on the UI thread after Execute() completes, so the
        /// ViewModel can safely read outputs and refresh bound collections.</summary>
        public event Action? RequestCompleted;

        public AutomatedSectionPlacerHandler(UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            // Esc / Cancel with a Yes-No confirm while views load or are placed (opening sheets is instant).
            using ToolCancellation cancel = Request == AutomatedSectionPlacerRequest.OpenSheets
                ? null
                : ToolCancellation.BeginFor(ToolCatalog.AutomatedSectionPlacer, app);
            try
            {
                switch (Request)
                {
                    case AutomatedSectionPlacerRequest.LoadViews:
                        ExecuteLoadViews();
                        break;
                    case AutomatedSectionPlacerRequest.PlaceViews:
                        ExecutePlaceViews();
                        break;
                    case AutomatedSectionPlacerRequest.OpenSheets:
                        ExecuteOpenSheets();
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // The placement transaction was rolled back as the exception unwound.
                Logs.Add(new LogEntry(LogLevel.Warning, "Cancelled by user. No views were placed."));
                LastRunSucceeded = false;
                throw; // ToolGuard treats it as a user cancel; finally still completes the request
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry(LogLevel.Error, $"Unhandled error in {Request}: {ex.Message}"));
                LastRunSucceeded = false;
            }
            finally
            {
                RequestCompleted?.Invoke();
            }
        }

        public string GetName() => "AutomatedSectionPlacer Orchestrator";

        // ─────────────────────────────────────────────────────────────
        // LOAD VIEWS (Stage 1) — AutomatedSectionPlacer V001: scoped to the
        // section views actually VISIBLE on the active Plan View (their
        // section-marker/OST_Viewers graphics appear on that plan), not
        // every view in the project (that's V222's job). See class remarks.
        // ─────────────────────────────────────────────────────────────
        private void ExecuteLoadViews()
        {
            var doc = _uiDoc.Document;
            var planView = doc.GetElement(PlanViewId) as ViewPlan;
            PlanViewName = planView?.Name ?? string.Empty;

            if (planView == null)
            {
                Logs.Add(new LogEntry(LogLevel.Error, "LoadViews: the plan view this tool was launched from no longer exists. Close and re-run the tool."));
                LastRunSucceeded = false;
                LoadedViews = new List<ViewInfo>();
                LoadedTitleblocks = new List<TitleblockOption>();
                LoadedExistingSheets = new List<ExistingSheetOption>();
                return;
            }

            Logs.Add(new LogEntry(LogLevel.Info, $"LoadViews: scanning section markers visible on plan view '{planView.Name}'."));

            LoadedViews = new List<ViewInfo>();
            LoadedTitleblocks = new List<TitleblockOption>();

            // View-scoped collector: OST_Viewers returns the graphical marker
            // elements (section heads/elevation tags/callout boundaries) drawn
            // ON this specific plan view — for a normal (non-reference)
            // section, the marker IS castable straight to the ViewSection it
            // represents. Reference sections (ViewSection.CreateReferenceSection)
            // have a separate FamilyInstance marker with no such direct
            // back-link — OfType<ViewSection>() below excludes those cleanly
            // rather than throwing, since they just don't match the cast.
            var markers = new FilteredElementCollector(doc, planView.Id)
                .OfCategory(BuiltInCategory.OST_Viewers)
                .WhereElementIsNotElementType()
                .ToElements();

            var views = markers
                .OfType<ViewSection>()
                .Where(v => v.ViewType == ViewType.Section
                         && !v.IsTemplate
                         && v.CanBePrinted
                         // Excludes dependent split-segment views (a long section
                         // split into pieces) — only the primary section is
                         // offered for placement, mirroring APUS's
                         // SectionCollectionService.Collect() pattern.
                         && v.GetPrimaryViewId() == ElementId.InvalidElementId)
                .Cast<View>()
                .ToList();

            Logs.Add(new LogEntry(LogLevel.Info, $"LoadViews: {views.Count} section view(s) detected on '{planView.Name}' (excluding templates, non-printable, split-segment dependents)."));

            foreach (var v in views)
            {
                ToolCancellation.CheckCurrent();
                try
                {
                    var (widthMm, heightMm, cropWidthFeet, cropHeightFeet) = ComputeSizeOnSheetMm(v);
                    var (rightDir, upDir, cropCenter, isResolved) = ResolveReadingOrderGeometry(v);

                    if (!isResolved)
                    {
                        Logs.Add(new LogEntry(LogLevel.Warning,
                            $"LoadViews: view '{v.Name}' has no active crop box — reading-order position unresolved, will sort last within its group."));
                    }

                    bool isAlreadyPlaced;
                    try
                    {
                        // V213 FIX: the real Autodesk.Revit.DB.ViewPlacementOnSheetStatus
                        // enum has 4 members — NotApplicable, NotPlaced,
                        // PartiallyPlaced, CompletelyPlaced — not a simple
                        // OnSheet/NotOnSheet as originally (incorrectly)
                        // assumed. Confirmed with Rafi: both PartiallyPlaced
                        // and CompletelyPlaced count as "Placed"; only
                        // NotPlaced/NotApplicable count as "Not Placed".
                        var status = v.GetPlacementOnSheetStatus();
                        isAlreadyPlaced = status == ViewPlacementOnSheetStatus.PartiallyPlaced
                                       || status == ViewPlacementOnSheetStatus.CompletelyPlaced;
                    }
                    catch
                    {
                        // Some view types (e.g. schedules on sheets in certain
                        // states) can throw here — default to "not placed"
                        // rather than dropping the whole view from LoadViews
                        // over a placement-status check failure.
                        isAlreadyPlaced = false;
                    }

                    var info = new ViewInfo(
                        viewId: v.Id,
                        name: v.Name,
                        revitViewType: v.ViewType,
                        viewTypeLabel: ViewTypeLabelHelper.Label(v.ViewType),
                        scale: SafeScale(v),
                        widthMm: widthMm,
                        heightMm: heightMm,
                        cropWidthFeet: cropWidthFeet,
                        cropHeightFeet: cropHeightFeet,
                        rightDirection: rightDir,
                        upDirection: upDir,
                        cropCenterModel: cropCenter,
                        isMarkerResolved: isResolved,
                        isAlreadyPlaced: isAlreadyPlaced,
                        // Pre-checked by default — this tool's whole point is
                        // "everything visible on the plan, ready to place",
                        // mixed placed/unplaced included (confirmed: re-placing
                        // an already-placed section always MOVES it).
                        isSelected: true,
                        parameterValues: CollectParameterValues(v));
                    LoadedViews.Add(info);
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    Logs.Add(new LogEntry(LogLevel.Warning, $"LoadViews: skipped view '{v.Name}' — {ex.Message}"));
                }
            }

            Logs.Add(new LogEntry(LogLevel.Info, $"LoadViews: {LoadedViews.Count} views ready for selection."));

            var titleblockSymbols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .ToList();

            foreach (var ts in titleblockSymbols)
            {
                try
                {
                    var bbox = ts.get_BoundingBox(null);
                    double wMm = 0, hMm = 0;
                    if (bbox != null)
                    {
                        wMm = FeetToMm(bbox.Max.X - bbox.Min.X);
                        hMm = FeetToMm(bbox.Max.Y - bbox.Min.Y);
                    }
                    string name = $"{ts.Family.Name} - {ts.Name}";
                    LoadedTitleblocks.Add(new TitleblockOption(ts.Id, name, wMm, hMm));
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    Logs.Add(new LogEntry(LogLevel.Warning, $"LoadViews: skipped titleblock '{ts.Name}' — {ex.Message}"));
                }
            }

            Logs.Add(new LogEntry(LogLevel.Info, $"LoadViews: {LoadedTitleblocks.Count} titleblock types loaded."));

            LoadedExistingSheets = new List<ExistingSheetOption>();
            var existingSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var s in existingSheets)
                LoadedExistingSheets.Add(new ExistingSheetOption(s.Id, s.SheetNumber, s.Name));

            Logs.Add(new LogEntry(LogLevel.Info, $"LoadViews: {LoadedExistingSheets.Count} existing sheet(s) available as placement targets."));
            LastRunSucceeded = true;
        }

        // ─────────────────────────────────────────────────────────────
        // PLACE VIEWS (Stage 3) — single Transaction for the entire run
        // ─────────────────────────────────────────────────────────────
        private void ExecutePlaceViews()
        {
            var doc = _uiDoc.Document;
            PlacedCount = 0;
            SkippedCount = 0;
            FailedCount = 0;
            FailedSheetCount = 0;
            _processedCount = 0;
            _totalToProcess = SheetsToPlace.Sum(sh => sh.Placements.Count);

            if (TitleblockFamilySymbolId == null)
            {
                Logs.Add(new LogEntry(LogLevel.Error, "PlaceViews: no titleblock selected. Aborting."));
                LastRunSucceeded = false;
                return;
            }

            Logs.Add(new LogEntry(LogLevel.Info, $"PlaceViews: starting placement of {SheetsToPlace.Count} sheet(s)."));

            using var tx = new Transaction(doc, "Automated Section Placer - Place Views");
            try
            {
                tx.Start();

                var titleblockSymbol = doc.GetElement(TitleblockFamilySymbolId) as FamilySymbol;
                if (titleblockSymbol != null && !titleblockSymbol.IsActive)
                    titleblockSymbol.Activate();

                ReportProgress(0, string.Empty);

                foreach (var sheet in SheetsToPlace)
                {
                    ToolCancellation.CheckCurrent();
                    int processedBeforeSheet = _processedCount;
                    try
                    {
                        ViewSheet targetSheet;
                        double extraOffsetYMm = 0;

                        if (sheet.ExistingSheetId != null)
                        {
                            // AutomatedSectionPlacer V001: manual "existing sheet" target
                            // (confirmed: user picks explicitly per group, never
                            // auto-filled) — place onto this sheet instead of creating
                            // a new one.
                            targetSheet = doc.GetElement(sheet.ExistingSheetId) as ViewSheet
                                ?? throw new InvalidOperationException($"Existing sheet {sheet.ExistingSheetLabel} no longer exists.");

                            sheet.CreatedSheetId = targetSheet.Id;
                            sheet.AssignedSheetNumber = targetSheet.SheetNumber;

                            Logs.Add(new LogEntry(LogLevel.Info,
                                $"PlaceViews: targeting existing sheet {targetSheet.SheetNumber} \"{targetSheet.Name}\"."));
                        }
                        else
                        {
                            var newSheet = ViewSheet.Create(doc, TitleblockFamilySymbolId);
                            newSheet.Name = sheet.GeneratedName;
                            // Sheet Number auto-increments by Revit's own numbering rules
                            // when left as the default assigned value; if a specific
                            // scheme is required, override newSheet.SheetNumber here
                            // using the last-used project sheet number + 1.

                            sheet.CreatedSheetId = newSheet.Id;
                            sheet.AssignedSheetNumber = newSheet.SheetNumber;
                            targetSheet = newSheet;

                            Logs.Add(new LogEntry(LogLevel.Info,
                                $"PlaceViews: created sheet {newSheet.SheetNumber} \"{sheet.GeneratedName}\"."));
                        }

                        // V213 FIX: previously, viewport offsets were converted straight
                        // from OffsetXMm/OffsetYMm into Revit XYZ, treating Revit's sheet
                        // origin (0,0) as if it were the usable-area's top-left corner.
                        // That is not generally true — a titleblock's own insertion point
                        // can sit anywhere relative to its bounding box, varying by family.
                        // ViewSheet.Create() auto-places one titleblock FamilyInstance on
                        // the new sheet; we look it up here (once per sheet, not per
                        // viewport) and read its REAL bounding box on this sheet, then
                        // anchor every viewport offset to that box's actual min corner
                        // (+ MarginLeftMm/MarginTopMm), which is robust regardless of the
                        // titleblock family's own origin convention. Works identically
                        // for an existing sheet's own titleblock instance.
                        var usableOrigin = ResolveUsableAreaOrigin(doc, targetSheet);
                        if (usableOrigin == null)
                        {
                            Logs.Add(new LogEntry(LogLevel.Warning,
                                $"PlaceViews: could not locate titleblock instance on sheet {targetSheet.SheetNumber} — falling back to sheet origin (0,0); viewport positions may be off if this titleblock's origin isn't at its bounding box corner."));
                        }

                        if (sheet.ExistingSheetId != null)
                        {
                            // V001 first-iteration approach (flagged as a known
                            // limitation): rather than a full obstacle-aware repack,
                            // stack the new group's packed layout BELOW whatever this
                            // existing sheet's own viewports already occupy — simple,
                            // deterministic, avoids overlap for the common case of
                            // existing content occupying the top of the sheet. Does not
                            // account for non-viewport clutter (schedules, text, etc.).
                            extraOffsetYMm = ComputeExistingContentBottomOffsetMm(doc, targetSheet, usableOrigin ?? XYZ.Zero);
                            if (extraOffsetYMm > 0)
                            {
                                Logs.Add(new LogEntry(LogLevel.Info,
                                    $"PlaceViews: sheet {targetSheet.SheetNumber} already has content — new views will stack below it ({extraOffsetYMm:0} mm down)."));
                            }
                        }

                        foreach (var placement in sheet.Placements)
                        {
                            ToolCancellation.CheckCurrent();
                            PlaceSingleViewport(doc, targetSheet, placement, usableOrigin ?? XYZ.Zero, extraOffsetYMm);
                            ReportProgress(1, placement.View.Name);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // user cancel: never swallowed as a failure
                    }
                    catch (Exception ex)
                    {
                        // Whatever this sheet had not yet reported is now settled
                        // (failed), so the progress bar still reaches its total.
                        ReportProgress(sheet.Placements.Count - (_processedCount - processedBeforeSheet), sheet.GeneratedName);

                        // V213 fix: sheet creation itself failed — every queued
                        // placement on this sheet counts as a failed view, not
                        // a single generic failure, so FailedCount matches what
                        // the Stage 5 summary claims ("Z failed" = views).
                        FailedSheetCount++;
                        FailedCount += sheet.Placements.Count;
                        Logs.Add(new LogEntry(LogLevel.Error, $"PlaceViews: failed to create sheet for \"{sheet.GeneratedName}\" — {ex.Message} ({sheet.Placements.Count} view(s) not placed)."));
                    }
                }

                ToolCancellation.CheckCurrent(); // last chance to cancel before the commit
                tx.Commit();
                LastRunSucceeded = true;
                Logs.Add(new LogEntry(LogLevel.Success,
                    $"PlaceViews complete: {SheetsToPlace.Count} sheet(s) attempted ({FailedSheetCount} failed) | {PlacedCount} view(s) placed | {SkippedCount} skipped | {FailedCount} failed."));
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                LastRunSucceeded = false;
                Logs.Add(new LogEntry(LogLevel.Error, $"PlaceViews: transaction rolled back — {ex.Message}"));
            }
        }

        /// <summary>
        /// V213: locates the titleblock FamilyInstance that ViewSheet.Create()
        /// auto-placed on the given sheet, reads its REAL bounding box on that
        /// sheet (robust to any titleblock family's own origin convention —
        /// never assumes the instance's insertion point equals its bounding
        /// box corner), and returns the usable-area's top-left corner in
        /// Revit model space: titleblock's actual min corner, shifted inward
        /// by MarginLeftMm (X) and inward from the max Y by MarginTopMm
        /// (Revit sheet-space Y increases upward, so "top" is the box's
        /// MAXIMUM Y, not its minimum).
        /// Returns null if no titleblock instance is found on the sheet
        /// (should not normally happen immediately after ViewSheet.Create,
        /// but guarded rather than throwing, per our fail-soft logging
        /// convention) — caller falls back to XYZ.Zero with a Warning logged.
        /// </summary>
        private XYZ? ResolveUsableAreaOrigin(Document doc, ViewSheet sheet)
        {
            var titleblockInstance = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            if (titleblockInstance == null)
                return null;

            var bbox = titleblockInstance.get_BoundingBox(sheet);
            if (bbox == null)
                return null;

            double marginLeftFeet = MmToFeet(MarginLeftMm);
            double marginTopFeet = MmToFeet(MarginTopMm);

            double usableOriginX = bbox.Min.X + marginLeftFeet;
            double usableOriginY = bbox.Max.Y - marginTopFeet;

            return new XYZ(usableOriginX, usableOriginY, 0);
        }

        /// <summary>
        /// AutomatedSectionPlacer V001: for an EXISTING-sheet target, finds
        /// how far down (in mm, relative to usableAreaOrigin, Y growing
        /// downward — same convention as ViewPlacement.OffsetYMm) this
        /// sheet's current viewports already extend, so newly packed views
        /// can be stacked below them instead of overlapping. Ignores the
        /// titleblock's own viewport (there isn't one — titleblocks are a
        /// separate category) and any viewport whose box outline can't be
        /// read. Returns 0 for an empty sheet (new content starts right at
        /// the usable-area top, same as a brand-new sheet).
        /// </summary>
        private double ComputeExistingContentBottomOffsetMm(Document doc, ViewSheet sheet, XYZ usableAreaOrigin)
        {
            double maxBottomMm = 0;
            var existingViewports = new FilteredElementCollector(doc, sheet.Id)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .ToList();

            foreach (var vp in existingViewports)
            {
                try
                {
                    var outline = vp.GetBoxOutline();
                    // Sheet-space Y increases upward; usableAreaOrigin.Y is the
                    // usable area's TOP edge, so "how far down" = origin.Y - box's
                    // minimum Y (its lowest/bottom edge).
                    double bottomFeet = usableAreaOrigin.Y - outline.MinimumPoint.Y;
                    double bottomMm = FeetToMm(bottomFeet);
                    if (bottomMm > maxBottomMm)
                        maxBottomMm = bottomMm;
                }
                catch
                {
                    // Skip a viewport whose outline can't be read — worst case
                    // this understates the occupied region for that one item.
                }
            }

            return maxBottomMm > 0 ? maxBottomMm + MarginBottomMm : 0;
        }

        /// <summary>
        /// Three-pass viewport placement (Create default -> Read actual box
        /// outline via GetBoxOutline() -> Move to the real computed offset),
        /// since Viewport.Create() does not accept a target position directly
        /// and its initial placement does not respect our packed X/Y offset.
        /// Silent skip (Warning log, no dialog) if the view cannot be placed
        /// (e.g. already on another sheet), per our logging convention.
        /// V213 FIX: usableAreaOrigin is the titleblock's REAL usable-area
        /// top-left corner in Revit model space (see ResolveUsableAreaOrigin)
        /// — OffsetXMm/OffsetYMm are relative to THIS point, not to Revit's
        /// sheet origin (0,0), which was the root cause of viewports landing
        /// outside the sheet border entirely.
        /// extraOffsetYMm (V001, AutomatedSectionPlacer): additional downward
        /// shift applied on top of OffsetYMm, used when targeting an existing
        /// sheet that already has content (see ComputeExistingContentBottomOffsetMm).
        /// Zero for a brand-new sheet.
        /// </summary>
        private void PlaceSingleViewport(Document doc, ViewSheet sheet, ViewPlacement placement, XYZ usableAreaOrigin, double extraOffsetYMm = 0)
        {
            var view = doc.GetElement(placement.View.ViewId) as View;
            if (view == null)
            {
                SkippedCount++;
                Logs.Add(new LogEntry(LogLevel.Warning, $"PlaceViews: view '{placement.View.Name}' no longer exists — skipped."));
                return;
            }

            // V001 (AutomatedSectionPlacer): re-placing an already-placed
            // section always MOVES it (confirmed — never duplicates). Delete
            // its existing viewport first, wherever it currently sits — this
            // is what clears Viewport.CanAddViewToSheet's block below, which
            // otherwise refuses any view that already has a viewport anywhere.
            if (placement.View.IsAlreadyPlaced)
            {
                var existingViewport = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .FirstOrDefault(v => v.ViewId == view.Id);
                if (existingViewport != null)
                {
                    var oldSheet = doc.GetElement(existingViewport.SheetId) as ViewSheet;
                    doc.Delete(existingViewport.Id);
                    Logs.Add(new LogEntry(LogLevel.Info,
                        $"PlaceViews: '{placement.View.Name}' was already placed on sheet {oldSheet?.SheetNumber ?? "?"} — moving it to {sheet.SheetNumber}."));
                }
            }

            // V001 (AutomatedSectionPlacer): apply the user's scale override
            // (if any) to the real view BEFORE placing it — ViewInfo.WidthMm/
            // HeightMm (and therefore the packed OffsetXMm/OffsetYMm below)
            // were already computed at this scale in Stage 2, so the viewport
            // sizing and the view's actual Scale property stay consistent.
            if (placement.View.ScaleOverride is int overrideScale && overrideScale > 0 && view.Scale != overrideScale)
            {
                try
                {
                    view.Scale = overrideScale;
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    Logs.Add(new LogEntry(LogLevel.Warning, $"PlaceViews: could not apply scale override 1:{overrideScale} to '{placement.View.Name}' — {ex.Message}"));
                }
            }

            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
            {
                SkippedCount++;
                Logs.Add(new LogEntry(LogLevel.Warning, $"PlaceViews: view '{placement.View.Name}' cannot be placed on this sheet (already placed elsewhere?) — skipped."));
                return;
            }

            // Pass 1: Create at a default point near the sheet origin.
            var defaultPoint = new XYZ(0, 0, 0);
            Viewport vp;
            try
            {
                vp = Viewport.Create(doc, sheet.Id, view.Id, defaultPoint);
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                FailedCount++;
                Logs.Add(new LogEntry(LogLevel.Warning, $"PlaceViews: could not create viewport for '{placement.View.Name}' — {ex.Message}"));
                return;
            }

            // Pass 2: Read actual box outline to know the viewport's real
            // extents (title block/label offsets vary the actual bounding
            // box vs. the raw view crop size).
            var outline = vp.GetBoxOutline();
            var currentCenter = (outline.MinimumPoint + outline.MaximumPoint) / 2.0;

            // Pass 3: Move to the real computed target position. OffsetXMm/
            // OffsetYMm are the view's TOP-LEFT corner within the usable area
            // (confirmed against ReadingOrderPackingService's cursor-based
            // row layout — cursorX/rowTop are left/top edges, not centers),
            // relative to the usable-area's top-left corner (Y growing
            // DOWNWARD in the packing model). Revit's MoveElement/viewport
            // center math needs the view's CENTER, so half of WidthMm/
            // HeightMm is added before converting to Revit sheet-space
            // (Y increasing upward, hence usableAreaOrigin.Y - offsetY).
            double offsetXFeet = MmToFeet(placement.OffsetXMm + placement.View.WidthMm / 2.0);
            double offsetYFeet = MmToFeet(extraOffsetYMm + placement.OffsetYMm + placement.View.HeightMm / 2.0);
            var targetCenter = new XYZ(
                usableAreaOrigin.X + offsetXFeet,
                usableAreaOrigin.Y - offsetYFeet,
                0);

            var translation = targetCenter - currentCenter;
            ElementTransformUtils.MoveElement(doc, vp.Id, translation);

            PlacedCount++;
            Logs.Add(new LogEntry(LogLevel.Info, $"PlaceViews: placed '{placement.View.Name}' at {placement.Position} on sheet {sheet.SheetNumber}."));
        }

        // ─────────────────────────────────────────────────────────────
        // OPEN SHEETS (Stage 4)
        // ─────────────────────────────────────────────────────────────
        private void ExecuteOpenSheets()
        {
            var doc = _uiDoc.Document;
            Logs.Add(new LogEntry(LogLevel.Info, $"OpenSheets: opening {SheetIdsToOpen.Count} selected sheet(s)."));

            foreach (var id in SheetIdsToOpen)
            {
                try
                {
                    var view = doc.GetElement(id) as View;
                    if (view != null)
                        _uiDoc.ActiveView = view;
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    Logs.Add(new LogEntry(LogLevel.Warning, $"OpenSheets: could not open sheet {id} — {ex.Message}"));
                }
            }

            LastRunSucceeded = true;
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────
        private static double FeetToMm(double feet) => feet * 304.8;
        private static double MmToFeet(double mm) => mm / 304.8;

        private (double widthMm, double heightMm, double cropWidthFeet, double cropHeightFeet) ComputeSizeOnSheetMm(View v)
        {
            var cropBox = v.CropBox;
            double widthFeet = cropBox.Max.X - cropBox.Min.X;
            double heightFeet = cropBox.Max.Y - cropBox.Min.Y;

            int scale = SafeScale(v);
            double scaleFactor = scale > 0 ? scale : 1;

            // Size on sheet = crop box size (model space) / view scale, converted to mm.
            double widthMm = FeetToMm(widthFeet) / scaleFactor;
            double heightMm = FeetToMm(heightFeet) / scaleFactor;
            return (widthMm, heightMm, widthFeet, heightFeet);
        }

        /// <summary>
        /// V213: resolves the reading-order sort anchor for a view — crop box
        /// center in model space (X,Y), plus the view's Right/Up direction
        /// vectors used to project that center onto the U/V axes in
        /// ReadingOrderPackingService.
        ///
        /// Confirmed with Rafi: View.CropBox always returns a box (never
        /// null), even for views with no crop applied — it silently returns
        /// a large default box in that case. The real signal for "does this
        /// view have a usable crop" is View.CropBoxActive. When false, this
        /// returns isResolved=false and the view is logged as a Warning and
        /// pushed to the end of the reading-order sort (SortFallback) —
        /// still placed, never skipped or failed.
        /// </summary>
        private (XYZ? rightDirection, XYZ? upDirection, XYZ? cropCenterModel, bool isResolved) ResolveReadingOrderGeometry(View v)
        {
            try
            {
                if (!v.CropBoxActive)
                    return (null, null, null, false);

                var cropBox = v.CropBox; // in the view's own local coordinate system
                var localCenter = (cropBox.Min + cropBox.Max) / 2.0;

                // CropBox.Transform maps the view's local crop-box coordinates
                // into model space — required to get a true model-space center,
                // since cropBox.Min/Max alone are in the view's local frame.
                var modelCenter = cropBox.Transform.OfPoint(localCenter);

                return (v.RightDirection, v.UpDirection, modelCenter, true);
            }
            catch
            {
                // Some view types (e.g. schedules, legends) can throw on
                // RightDirection/UpDirection access — treat as unresolved
                // rather than letting the whole LoadViews pass fail for
                // this one view.
                return (null, null, null, false);
            }
        }

        private static int SafeScale(View v)
        {
            try
            {
                if (v.ViewType == ViewType.ThreeD || v.ViewType == ViewType.Legend || v.ViewType == ViewType.Schedule)
                    return 0;
                return v.Scale;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Reads every parameter this View element carries — built-in, project,
        /// and shared parameters bound to the Views category all show up the
        /// same way in View.Parameters — as a Name -> display-string map.
        /// Feeds Stage 1's generic Parameter/Value filter. Any parameter that
        /// throws while being read (unusual, but seen with some read-only
        /// calculated params) is skipped rather than failing the whole view.
        /// </summary>
        private static Dictionary<string, string> CollectParameterValues(View v)
        {
            var values = new Dictionary<string, string>();
            foreach (Parameter p in v.Parameters)
            {
                try
                {
                    var name = p?.Definition?.Name;
                    if (string.IsNullOrWhiteSpace(name) || values.ContainsKey(name)) continue;

                    string? display = p!.HasValue ? p.AsValueString() : null;
                    if (string.IsNullOrEmpty(display) && p.StorageType == StorageType.String)
                        display = p.AsString();

                    values[name] = display ?? string.Empty;
                }
                catch
                {
                    // Skip unreadable parameters — never fails the whole view load over one.
                }
            }
            return values;
        }
    }
}
