using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.PlanFromScopeBox.V006.Core.Models;
using Revit26_Plugin_Revised.PlanFromScopeBox.V006.Core.Services;
using Revit26_Plugin_Revised.PlanFromScopeBox.V006.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.PlanFromScopeBox.V006.Core.Engine
{
    /// <summary>Input parameters for a generation run, gathered from the ViewModel.</summary>
    public sealed class GenerationRequest
    {
        public IReadOnlyList<ScopeBoxRow> SelectedRows { get; init; } = new List<ScopeBoxRow>();
        public ElementId LevelId { get; init; }
        public ElementId TitleBlockTypeId { get; init; }
        public int ScaleDenominator { get; init; } = 100;
        public double MarginMm { get; init; } = 10;
        public double GutterMm { get; init; } = 8;

        /// <summary>Sheet size from the measured title block (mm). Defaults to A0 when the
        /// measure was unavailable; SheetLayout.Usable also guards zero values.</summary>
        public double SheetWidthMm { get; init; } = SheetLayout.A0WidthMm;
        public double SheetHeightMm { get; init; } = SheetLayout.A0HeightMm;

        /// <summary>Title-block clear-zone width along the sheet's right edge (mm).</summary>
        public double TbClearRightMm { get; init; }

        /// <summary>Title-block clear-zone height along the sheet's bottom edge (mm).</summary>
        public double TbClearBottomMm { get; init; }

        public bool OpenSheetsAfter { get; init; }

        public string SheetNumberPattern { get; init; } = "AP-{Increment}";
        public string SheetNamePattern { get; init; } = "{ScopeBoxName}";
        public string ViewNumberPattern { get; init; } = "{Increment}";
        public string ViewNamePattern { get; init; } = "{ScopeBoxName} - Plan";
    }

    /// <summary>
    /// Aggregate outcome of a run. Metrics are DERIVED from <see cref="Results"/> so there is
    /// a single source of truth rather than separately incremented counters.
    /// </summary>
    public sealed class GenerationSummary
    {
        public List<ElementId> CreatedSheetIds { get; } = new();
        public List<PlanCreationResult> Results { get; } = new();

        public int PlansCreated =>
            Results.Count(r => r.Outcome is PlanOutcome.Placed or PlanOutcome.PlacedOversize);
        public int SheetsCreated => CreatedSheetIds.Count;
        public int Skipped => Results.Count(r => r.Outcome == PlanOutcome.Skipped);
    }

    /// <summary>
    /// Core generator. Runs inside a single TransactionGroup so the whole run is one atomic
    /// undo. Phase 1 (CreateViews) creates a plan view per selected scope box, assigns the
    /// scope box (crop follows the box), applies scale, and resolves BOTH the view name and
    /// view number together (so {Increment} stays in sync between them). Phase 2 (PlaceSheets)
    /// bin-packs the views onto sheets, creates each sheet, places viewports at margin/clear-
    /// zone-offset sheet coordinates, and applies each stored view number as the viewport's
    /// Detail Number.
    /// </summary>
    public sealed class PlanSheetGenerator
    {
        private const double MmToFeet = 1.0 / 304.8;

        private readonly Document _doc;
        private readonly Action<LogLevel, string> _log;

        public PlanSheetGenerator(Document doc, Action<LogLevel, string> log)
        {
            _doc = doc;
            _log = log ?? ((_, __) => { });
        }

        public GenerationSummary Run(GenerationRequest req)
        {
            var summary = new GenerationSummary();

            ViewFamilyType planVft = ResolveFloorPlanVft();
            if (planVft == null)
            {
                _log(LogLevel.Error, "No Floor Plan view type found in document. Aborting.");
                return summary;
            }

            var naming = new NamingResolver();
            SeedNaming(naming);

            var (usableW, usableH) = SheetLayout.Usable(
                req.SheetWidthMm, req.SheetHeightMm,
                req.MarginMm, req.TbClearRightMm, req.TbClearBottomMm);

            if (usableW <= 0 || usableH <= 0)
            {
                _log(LogLevel.Error, "Usable sheet area is zero or negative — check margin / title-block clear zone.");
                return summary;
            }
            var packer = new SheetBinPacker(usableW, usableH, req.GutterMm);

            using var tg = new TransactionGroup(_doc, "Plan From Scope Box");
            tg.Start();

            var viewByScopeBox = new Dictionary<long, ElementId>();
            List<PackItem> packItems = CreateViews(req, planVft, naming, usableW, usableH, summary, viewByScopeBox);

            List<PackedSheet> sheets = packer.Pack(packItems);
            PlaceSheets(req, sheets, naming, viewByScopeBox, summary);

            ToolCancellation.CheckCurrent(); // last chance to cancel before the group is kept
            tg.Assimilate();
            return summary;
        }

        // ── Phase 1 ─────────────────────────────────────────────────────────────────
        private List<PackItem> CreateViews(
            GenerationRequest req, ViewFamilyType planVft, NamingResolver naming,
            double usableW, double usableH,
            GenerationSummary summary, Dictionary<long, ElementId> viewByScopeBox)
        {
            var packItems = new List<PackItem>();

            using var tView = new Transaction(_doc, "Create plan views");
            ApplyFailurePreprocessor(tView);
            tView.Start();

            foreach (ScopeBoxRow row in req.SelectedRows)
            {
                ToolCancellation.CheckCurrent();
                try
                {
                    ViewPlan view = ViewPlan.Create(_doc, planVft.Id, req.LevelId);
                    view.Scale = req.ScaleDenominator;

                    // Assign scope box -> crop follows the box.
                    var sbId = new ElementId(row.ScopeBoxId);
                    Parameter sbParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (sbParam != null && !sbParam.IsReadOnly)
                        sbParam.Set(sbId);

                    view.CropBoxActive = true;
                    view.CropBoxVisible = true;

                    // Regenerate so the scope-box-driven crop is realised before anything
                    // downstream relies on the view's geometry.
                    _doc.Regenerate();

                    // Resolve number FIRST (advances the view counter), then the name — so
                    // {Increment} in the name matches the number just drawn (D).
                    string vNum = naming.ResolveViewNumber(req.ViewNumberPattern, row.Name, row.Level);
                    string vName = naming.ResolveViewName(req.ViewNamePattern, row.Name, row.Level);
                    SetViewName(view, vName, row.Name);

                    viewByScopeBox[row.ScopeBoxId] = view.Id;

                    var (wMm, hMm) = SheetLayout.Footprint(row.WidthM, row.HeightM, req.ScaleDenominator);
                    bool oversize = !SheetLayout.Fits(wMm, hMm, usableW, usableH);

                    packItems.Add(new PackItem
                    {
                        ScopeBoxId = row.ScopeBoxId,
                        ScopeBoxName = row.Name,
                        ViewNumber = vNum,
                        FootprintWmm = wMm,
                        FootprintHmm = hMm,
                        Oversize = oversize
                    });

                    summary.Results.Add(new PlanCreationResult
                    {
                        ScopeBoxName = row.Name,
                        Outcome = oversize ? PlanOutcome.PlacedOversize : PlanOutcome.Placed,
                        ViewId = view.Id.Value,
                        ViewName = vName
                    });

                    if (oversize)
                        _log(LogLevel.Warning, $"{row.Name}: plan created but exceeds usable sheet at 1:{req.ScaleDenominator} — own sheet.");
                    else
                        _log(LogLevel.Success, $"Plan created · {row.Name} → \"{vName}\"");
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    summary.Results.Add(new PlanCreationResult
                    {
                        ScopeBoxName = row.Name,
                        Outcome = PlanOutcome.Skipped,
                        Reason = ex.Message
                    });
                    _log(LogLevel.Warning, $"{row.Name}: view creation failed — skipped ({ex.Message}).");
                }
            }

            tView.Commit();
            return packItems;
        }

        // ── Phase 2 ─────────────────────────────────────────────────────────────────
        private void PlaceSheets(
            GenerationRequest req, List<PackedSheet> sheets, NamingResolver naming,
            Dictionary<long, ElementId> viewByScopeBox, GenerationSummary summary)
        {
            string levelName = req.SelectedRows.FirstOrDefault()?.Level ?? string.Empty;

            using var tSheet = new Transaction(_doc, "Create sheets & place views");
            ApplyFailurePreprocessor(tSheet);
            tSheet.Start();

            foreach (PackedSheet packed in sheets)
            {
                ToolCancellation.CheckCurrent();
                ViewSheet sheet;
                try
                {
                    sheet = ViewSheet.Create(_doc, req.TitleBlockTypeId);
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    _log(LogLevel.Error, $"Sheet creation failed — {ex.Message}");
                    continue;
                }

                string firstName = packed.Slots.FirstOrDefault()?.Item.ScopeBoxName ?? "Sheet";
                string sheetNum = naming.ResolveSheetNumber(req.SheetNumberPattern, firstName, levelName);
                string sheetName = naming.ResolveSheetName(req.SheetNamePattern, firstName, levelName);
                SetSheetIdentity(sheet, sheetNum, sheetName);

                int placedOnSheet = 0;
                foreach (PackSlot slot in packed.Slots)
                {
                    ToolCancellation.CheckCurrent();
                    if (!viewByScopeBox.TryGetValue(slot.Item.ScopeBoxId, out ElementId viewId))
                        continue;

                    try
                    {
                        // Packer coordinates are relative to the USABLE area origin; convert
                        // to sheet coordinates by offsetting margin (X/Y) and the bottom
                        // title-block clear strip (Y) (C).
                        double sheetXmm = req.MarginMm + slot.CentreXmm;
                        double sheetYmm = req.MarginMm + req.TbClearBottomMm + slot.CentreYmm;
                        XYZ centre = new XYZ(sheetXmm * MmToFeet, sheetYmm * MmToFeet, 0);

                        if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, viewId))
                        {
                            _log(LogLevel.Warning, $"{slot.Item.ScopeBoxName}: view can't be added to sheet (already placed?).");
                            continue;
                        }

                        Viewport vp = Viewport.Create(_doc, sheet.Id, viewId, centre);
                        placedOnSheet++;

                        // Apply the Phase-1 view number as the viewport Detail Number (D).
                        SetDetailNumber(vp, slot.Item.ViewNumber);

                        PlanCreationResult match = summary.Results
                            .FirstOrDefault(r => r.ViewId == viewId.Value && string.IsNullOrEmpty(r.SheetNumber));
                        if (match != null)
                            match.SheetNumber = sheetNum;
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // user cancel: never swallowed as a failure
                    }
                    catch (Exception ex)
                    {
                        _log(LogLevel.Warning, $"{slot.Item.ScopeBoxName}: viewport placement failed — {ex.Message}");
                    }
                }

                summary.CreatedSheetIds.Add(sheet.Id);
                string kind = packed.IsOversizeSheet ? " (oversize)" : "";
                _log(LogLevel.Success, $"Sheet {sheetNum} created{kind} · {placedOnSheet} view(s) packed");
            }

            tSheet.Commit();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────
        private ViewFamilyType ResolveFloorPlanVft() =>
            new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);

        private void SeedNaming(NamingResolver naming)
        {
            var existingSheetNums = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber);
            naming.ReserveExistingSheets(existingSheetNums);

            var existingViewNames = new FilteredElementCollector(_doc)
                .OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).Select(v => v.Name);
            naming.ReserveExistingViews(existingViewNames);
        }

        /// <summary>Attaches the targeted failure preprocessor to a transaction (E). The
        /// allowlist starts empty — specific benign-warning GUIDs are added as encountered,
        /// never blanket suppression. Set on the outer Transaction per convention (never on
        /// a SubTransaction, which does not support failure handling options).</summary>
        private static void ApplyFailurePreprocessor(Transaction t)
        {
            FailureHandlingOptions fho = t.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(new FailurePreprocessor());
            t.SetFailureHandlingOptions(fho);
        }

        /// <summary>Sets the view name, appending a numeric suffix only on genuine name
        /// collisions. Logs if a fallback suffix was required.</summary>
        private void SetViewName(View view, string desired, string scopeBoxName)
        {
            if (string.IsNullOrWhiteSpace(desired)) return;
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    view.Name = i == 0 ? desired : $"{desired} ({i})";
                    if (i > 0)
                        _log(LogLevel.Warning, $"{scopeBoxName}: view name \"{desired}\" taken — used \"{desired} ({i})\".");
                    return;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Duplicate name — try next suffix.
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    _log(LogLevel.Warning, $"{scopeBoxName}: could not set view name — {ex.Message}");
                    return;
                }
            }
            _log(LogLevel.Warning, $"{scopeBoxName}: exhausted name suffixes for \"{desired}\" — left default.");
        }

        private void SetSheetIdentity(ViewSheet sheet, string number, string name)
        {
            try { if (!string.IsNullOrWhiteSpace(number)) sheet.SheetNumber = number; }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex) { _log(LogLevel.Warning, $"Sheet number \"{number}\" rejected — {ex.Message}"); }

            try { if (!string.IsNullOrWhiteSpace(name)) sheet.Name = name; }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex) { _log(LogLevel.Warning, $"Sheet name \"{name}\" rejected — {ex.Message}"); }
        }

        private void SetDetailNumber(Viewport vp, string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return;
            try
            {
                Parameter p = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                if (p != null && !p.IsReadOnly) p.Set(number);
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                _log(LogLevel.Warning, $"Detail number \"{number}\" rejected — {ex.Message}");
            }
        }
    }
}
