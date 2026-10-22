using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.UI.ViewModels
{
    /// <summary>This part: the Delete unused and Line thickness tabs.</summary>
    public sealed partial class MainViewModel
    {
        private DeletePlan _pendingPlan;

        // ── Delete unused ─────────────────────────────────────────────────

        /// <summary>Delete the ticked unused line styles.</summary>
        [ObservableProperty] private bool deleteUnusedStyles = true;

        /// <summary>Delete duplicate / overlapping lines (one per cluster is kept).</summary>
        [ObservableProperty] private bool deleteDuplicates = true;

        /// <summary>Delete lines shorter than the tolerance.</summary>
        [ObservableProperty] private bool deleteShort;

        /// <summary>Skip (and log) lines in groups. Lines owned by other users are always skipped.</summary>
        [ObservableProperty] private bool skipBlocked = true;

        /// <summary>Which duplicate survives.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(KeepOldest), nameof(KeepNewest), nameof(KeepHeaviest))]
        private DuplicateKeepRule keepRule = DuplicateKeepRule.OldestId;

        /// <summary>Keep the oldest ID; bound to its radio button.</summary>
        public bool KeepOldest { get => KeepRule == DuplicateKeepRule.OldestId; set { if (value) KeepRule = DuplicateKeepRule.OldestId; } }

        /// <summary>Keep the newest ID; bound to its radio button.</summary>
        public bool KeepNewest { get => KeepRule == DuplicateKeepRule.NewestId; set { if (value) KeepRule = DuplicateKeepRule.NewestId; } }

        /// <summary>Keep the heaviest weight; bound to its radio button.</summary>
        public bool KeepHeaviest { get => KeepRule == DuplicateKeepRule.HeaviestWeight; set { if (value) KeepRule = DuplicateKeepRule.HeaviestWeight; } }

        /// <summary>True while the inline delete confirmation is shown.</summary>
        [ObservableProperty] private bool isConfirmVisible;

        /// <summary>Text of the inline delete confirmation.</summary>
        [ObservableProperty] private string confirmText = string.Empty;

        /// <summary>Reports what Delete all found would delete, without changing the model.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void PreviewDelete()
        {
            HideConfirm();
            var plan = BuildDeletePlan(VisibleRows().ToList());
            if (plan == null) return;
            Run("Preview…", app => new DetailLineActions(app.ActiveUIDocument.Document, Log).DeleteUnused(plan, dryRun: true));
        }

        /// <summary>Asks to delete the checked lines (and the ticked unused styles).</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void DeleteChecked() => AskDelete(CheckedVisibleRows().ToList(), "checked");

        /// <summary>Asks to delete every found line that matches the options (and the ticked unused styles).</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void DeleteAllFound() => AskDelete(VisibleRows().ToList(), "found");

        /// <summary>Runs the delete shown in the confirmation bar.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void ConfirmDelete()
        {
            var plan = _pendingPlan;
            HideConfirm();
            if (plan == null) return;
            Run("Deleting…", app =>
            {
                int deleted = new DetailLineActions(app.ActiveUIDocument.Document, Log).DeleteUnused(plan, dryRun: false);
                if (deleted > 0) RefreshAfterChange(app);
            });
        }

        /// <summary>Hides the confirmation bar without deleting.</summary>
        [RelayCommand]
        private void CancelDelete()
        {
            if (IsConfirmVisible) Log(LogLevel.Info, "Delete cancelled.");
            HideConfirm();
        }

        private void AskDelete(List<DetailLineRow> targets, string which)
        {
            HideConfirm();
            var plan = BuildDeletePlan(targets);
            if (plan == null) return;

            int lines = plan.Duplicates.Union(plan.ShortLines).Count();
            var byId = targets.ToDictionary(r => r.Id);
            int blocked = plan.Duplicates.Union(plan.ShortLines)
                .Count(id => byId.TryGetValue(id, out var r) && (r.IsOwnedByOther || (SkipBlocked && r.InGroup)));

            _pendingPlan = plan;
            ConfirmText = $"Delete {lines - blocked} {which} line(s) ({plan.Duplicates.Count} duplicate, {plan.ShortLines.Count} short) "
                + $"and {plan.Styles.Count} unused line style(s)? "
                + (blocked > 0 ? $"{blocked} line(s) in groups or owned by other users will be skipped. " : string.Empty)
                + "Ctrl+Z in Revit undoes it.";
            IsConfirmVisible = true;
        }

        // Null (logged) when nothing is selected for deletion.
        private DeletePlan BuildDeletePlan(List<DetailLineRow> targets)
        {
            var plan = new DeletePlan { SkipGroups = SkipBlocked };

            if (DeleteDuplicates)
            {
                // Keepers come from the whole cluster, so a checked keeper is never deleted.
                var keepers = Rows.Where(r => r.IsDuplicate).GroupBy(r => r.DuplicateCluster)
                    .ToDictionary(g => g.Key, g => DuplicateDetector.Keeper(g, KeepRule));
                plan.Duplicates.AddRange(targets.Where(r => r.IsDuplicate && !ReferenceEquals(keepers[r.DuplicateCluster], r)).Select(r => r.Id));
            }
            if (DeleteShort)
                plan.ShortLines.AddRange(targets.Where(r => r.IsShort).Select(r => r.Id));
            if (DeleteUnusedStyles)
                plan.Styles.AddRange(UnusedStyles.Where(s => s.IsChecked).Select(s => s.CategoryId));

            if (plan.Duplicates.Count == 0 && plan.ShortLines.Count == 0 && plan.Styles.Count == 0)
            {
                Log(LogLevel.Warning, "Nothing to delete: no duplicates, short lines or ticked unused styles match the options"
                    + (targets.Count == 0 ? " (no line is checked or found)." : "."));
                return null;
            }
            return plan;
        }

        private void HideConfirm()
        {
            _pendingPlan = null;
            IsConfirmVisible = false;
        }

        // ── Line thickness ────────────────────────────────────────────────

        /// <summary>Line thickness mode.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ModeAssignStyle), nameof(ModeStyleWeight), nameof(ModeViewOverride), nameof(IsWeightPickerVisible))]
        private ThicknessMode thicknessMode = ThicknessMode.AssignStyle;

        /// <summary>Mode = Assign line style; bound to its radio button.</summary>
        public bool ModeAssignStyle { get => ThicknessMode == ThicknessMode.AssignStyle; set { if (value) ThicknessMode = ThicknessMode.AssignStyle; } }

        /// <summary>Mode = Change style line weight; bound to its radio button.</summary>
        public bool ModeStyleWeight { get => ThicknessMode == ThicknessMode.StyleLineWeight; set { if (value) ThicknessMode = ThicknessMode.StyleLineWeight; } }

        /// <summary>Mode = Per-view override; bound to its radio button.</summary>
        public bool ModeViewOverride { get => ThicknessMode == ThicknessMode.ViewOverride; set { if (value) ThicknessMode = ThicknessMode.ViewOverride; } }

        /// <summary>True when the weight picker applies (every mode except Assign line style).</summary>
        public bool IsWeightPickerVisible => ThicknessMode != ThicknessMode.AssignStyle;

        /// <summary>Line weight for the weight modes (1–16).</summary>
        [ObservableProperty] private int weight = 3;

        /// <summary>Style for Assign line style.</summary>
        [ObservableProperty] private LineStyleItem selectedAssignStyle;

        /// <summary>Per-view override mode: clear existing overrides instead of setting one.</summary>
        [ObservableProperty] private bool clearOverrides;

        /// <summary>Project-wide warning for Change style line weight.</summary>
        [ObservableProperty] private string styleWeightWarning = string.Empty;

        partial void OnThicknessModeChanged(ThicknessMode value) => UpdateThicknessWarning();

        /// <summary>Applies the Line thickness mode to the checked lines.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void ApplyChecked() => ApplyThickness(CheckedVisibleRows().ToList(), "checked");

        /// <summary>Applies the Line thickness mode to every found line.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void ApplyAllFound() => ApplyThickness(VisibleRows().ToList(), "found");

        private void ApplyThickness(List<DetailLineRow> targets, string which)
        {
            HideConfirm();
            if (targets.Count == 0)
            {
                Log(LogLevel.Warning, $"No {which} line to change.");
                return;
            }

            var ids = targets.Select(r => r.Id).ToList();
            int weight = Weight;
            switch (ThicknessMode)
            {
                case ThicknessMode.AssignStyle:
                {
                    var style = SelectedAssignStyle;
                    if (style == null || style.GraphicsStyleId == null || style.GraphicsStyleId == ElementId.InvalidElementId)
                    {
                        Log(LogLevel.Warning, "Choose a line style to assign.");
                        return;
                    }
                    Run("Assigning line style…", app =>
                    {
                        if (new DetailLineActions(app.ActiveUIDocument.Document, Log).AssignStyle(ids, style.GraphicsStyleId) > 0)
                            RefreshAfterThicknessChange(app);
                    });
                    break;
                }
                case ThicknessMode.StyleLineWeight:
                {
                    var styleIds = targets.Select(r => r.StyleCategoryId).Where(id => id != null).Distinct().ToList();
                    Run("Changing line style weight…", app =>
                    {
                        if (new DetailLineActions(app.ActiveUIDocument.Document, Log).SetStyleWeight(styleIds, weight) > 0)
                            RefreshAfterThicknessChange(app);
                    });
                    break;
                }
                default:
                {
                    int? pen = ClearOverrides ? null : weight;
                    Run(ClearOverrides ? "Clearing overrides…" : "Setting overrides…", app =>
                    {
                        if (new DetailLineActions(app.ActiveUIDocument.Document, Log).SetViewOverride(ids, pen) > 0)
                            RefreshAfterThicknessChange(app);
                    });
                    break;
                }
            }
        }

        private void UpdateThicknessWarning()
        {
            if (ThicknessMode != ThicknessMode.StyleLineWeight)
            {
                StyleWeightWarning = string.Empty;
                return;
            }
            var visible = VisibleRows().ToList();
            int all = visible.Select(r => r.StyleCategoryId).Where(id => id != null).Distinct().Count();
            int checkedStyles = visible.Where(r => r.IsChecked).Select(r => r.StyleCategoryId).Where(id => id != null).Distinct().Count();
            StyleWeightWarning = $"Project-wide change: this sets the weight of the line style itself, so every line using it "
                + $"changes in every view, not only the lines listed here. Apply to all found changes {all} line style(s); "
                + $"Apply to checked changes {checkedStyles}.";
        }

        // Re-runs the search with the same scope after a successful change, inside the same API call.
        private void RefreshAfterChange(Autodesk.Revit.UI.UIApplication app)
        {
            var scope = Scope;
            double tol = ToleranceMm;
            var checkedIds = new HashSet<long>(Rows.Where(r => r.IsChecked).Select(r => r.IdValue));
            var result = DetailLineScanner.Scan(app.ActiveUIDocument, scope, tol, (_, _) => { });
            foreach (var r in result.Rows) r.IsChecked = checkedIds.Contains(r.IdValue);
            _dispatcher.Invoke(() => ApplyScan(result));
            Log(LogLevel.Info, $"Grid refreshed: {result.Rows.Count} line(s) in scope.");
        }

        // After a committed line-thickness change: refresh the grid, redraw the active view (Revit can
        // leave it stale after an Object Styles change made through the API) and warn when Thin Lines
        // mode hides the new weights.
        private void RefreshAfterThicknessChange(Autodesk.Revit.UI.UIApplication app)
        {
            RefreshAfterChange(app);

            var uiDoc = app.ActiveUIDocument;
            try
            {
                uiDoc.RefreshActiveView();
                Log(LogLevel.Info, $"Active view '{uiDoc.ActiveView?.Name}' redrawn. Other views update when you open them.");
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                Log(LogLevel.Warning, $"The change is saved, but the active view could not be redrawn ({ex.Message}). Pan or zoom to redraw it.");
            }

            bool thinLines;
            try { thinLines = Autodesk.Revit.UI.ThinLinesOptions.AreThinLinesEnabled; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return; }

            if (thinLines)
                Log(LogLevel.Warning, "Thin Lines is on (View tab → Thin Lines, or TL), so Revit draws every line at weight 1 "
                    + "and the new weights do not show on screen. They are saved; turn Thin Lines off to see them.");
            else
                Log(LogLevel.Info, "Thin Lines is off, so the new weights show on screen.");
        }
    }
}
