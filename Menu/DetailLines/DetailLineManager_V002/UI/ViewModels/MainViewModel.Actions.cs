using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models;
using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.UI.ViewModels
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

        /// <summary>Handle overlapping straight lines of the same style (off by default).</summary>
        [ObservableProperty] private bool deleteOverlaps;

        /// <summary>Delete covered lines or join overlapping ones.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OverlapDeleteCovered), nameof(OverlapJoin))]
        private OverlapMode overlapMode = OverlapMode.DeleteCovered;

        /// <summary>Overlap mode = delete covered lines; bound to its radio button.</summary>
        public bool OverlapDeleteCovered { get => OverlapMode == OverlapMode.DeleteCovered; set { if (value) OverlapMode = OverlapMode.DeleteCovered; } }

        /// <summary>Overlap mode = join into one line; bound to its radio button.</summary>
        public bool OverlapJoin { get => OverlapMode == OverlapMode.Join; set { if (value) OverlapMode = OverlapMode.Join; } }

        /// <summary>
        /// Line style name patterns that are never deleted, separated by ';' with * and ? wildcards
        /// (e.g. "A-*;S-*"). Use it for styles applied with the Linework tool, which Revit does not report.
        /// </summary>
        [ObservableProperty] private string keepStylePatterns = string.Empty;

        /// <summary>Note under the unused-style list (not counted yet / none), or empty.</summary>
        [ObservableProperty] private string unusedStylesHint = string.Empty;

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

        /// <summary>Asks to delete the checked lines (and the ticked unused styles), after checking what goes.</summary>
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
                var summary = new DetailLineActions(app.ActiveUIDocument.Document, Log).DeleteUnused(plan, dryRun: false);
                if (!summary.IsEmpty) RefreshAfterChange();
            });
        }

        /// <summary>Hides the confirmation bar without deleting.</summary>
        [RelayCommand]
        private void CancelDelete()
        {
            if (IsConfirmVisible) Log(LogLevel.Info, "Delete cancelled.");
            HideConfirm();
        }

        // Checks the plan in Revit first (a dry run that also measures the dimensions and tags that
        // would go with the lines), then shows the confirmation bar with those numbers.
        private void AskDelete(List<DetailLineRow> targets, string which)
        {
            HideConfirm();
            var plan = BuildDeletePlan(targets);
            if (plan == null) return;

            Run("Checking what would be deleted…", app =>
            {
                var summary = new DetailLineActions(app.ActiveUIDocument.Document, Log).DeleteUnused(plan, dryRun: true);
                _dispatcher.Invoke(() => ShowConfirm(plan, summary, which));
            });
        }

        private void ShowConfirm(DeletePlan plan, DeleteSummary summary, string which)
        {
            if (summary.IsEmpty)
            {
                Log(LogLevel.Warning, $"Nothing to delete among the {which} lines after checking ({summary.Skipped} skipped; see above).");
                return;
            }

            _pendingPlan = plan;
            ConfirmText = $"Delete {summary.CountsText} from the {which} lines? "
                + (summary.DependentCount > 0 ? $"Revit will also remove {summary.DependentsText} attached to them. " : string.Empty)
                + (summary.DependentsMeasured ? string.Empty : "Attached dimensions and tags could not be counted. ")
                + (summary.Skipped > 0 ? $"{summary.Skipped} item(s) will be skipped (see the log). " : string.Empty)
                + "Ctrl+Z in Revit undoes it.";
            IsConfirmVisible = true;
        }

        // Null (logged) when nothing is selected for deletion.
        private DeletePlan BuildDeletePlan(List<DetailLineRow> targets)
        {
            var plan = new DeletePlan { SkipGroups = SkipBlocked, ToleranceFeet = Math.Max(ToleranceMm, 0.001) / 304.8 };

            if (DeleteDuplicates)
            {
                // Keepers come from the whole cluster, so a checked keeper is never deleted.
                var keepers = Rows.Where(r => r.IsDuplicate).GroupBy(r => r.DuplicateCluster)
                    .ToDictionary(g => g.Key, g => DuplicateDetector.Keeper(g, KeepRule));
                plan.Duplicates.AddRange(targets.Where(r => r.IsDuplicate && !ReferenceEquals(keepers[r.DuplicateCluster], r)).Select(r => r.Id));
            }
            if (DeleteOverlaps && OverlapMode == OverlapMode.DeleteCovered)
                plan.Covered.AddRange(targets.Where(r => r.IsCovered).Select(r => r.Id));
            if (DeleteOverlaps && OverlapMode == OverlapMode.Join)
            {
                // A set is joined when any of its lines is a target; the whole set takes part.
                var targetGroups = new HashSet<int>(targets.Where(r => r.IsOverlapping).Select(r => r.OverlapGroup));
                foreach (var g in Rows.Where(r => targetGroups.Contains(r.OverlapGroup)).GroupBy(r => r.OverlapGroup))
                    plan.JoinGroups.Add(g.Select(r => r.Id).ToList());
            }
            if (DeleteShort)
                plan.ShortLines.AddRange(targets.Where(r => r.IsShort).Select(r => r.Id));
            if (DeleteUnusedStyles)
            {
                if (!UsageCounted)
                    Log(LogLevel.Warning, "Line style usage has not been counted yet, so no unused style is included. Wait for the count to finish.");
                plan.Styles.AddRange(UnusedStyles.Where(s => s.IsChecked && !s.IsKeptByPattern).Select(s => s.CategoryId));
            }

            if (plan.Duplicates.Count == 0 && plan.Covered.Count == 0 && plan.JoinGroups.Count == 0
                && plan.ShortLines.Count == 0 && plan.Styles.Count == 0)
            {
                Log(LogLevel.Warning, "Nothing to delete: no duplicates, overlaps, short lines or ticked unused styles match the options"
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

        // After a successful change: search the same scope again (in steps, cancellable) once the
        // current external-event call has finished, keeping the lines that were checked.
        private void RefreshAfterChange()
        {
            var checkedIds = new HashSet<long>(Rows.Where(r => r.IsChecked).Select(r => r.IdValue));
            Log(LogLevel.Info, "Refreshing the grid…");
            _dispatcher.BeginInvoke(new Action(() => StartScan(usageOnly: false, keepChecked: checkedIds)));
        }

        // Marks unused styles that match a keep pattern; they are unticked and never deleted.
        private void ApplyKeepPatterns()
        {
            var patterns = (KeepStylePatterns ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToList();

            foreach (var s in UnusedStyles)
            {
                bool kept = patterns.Any(r => r.IsMatch(s.Name));
                if (kept && !s.IsKeptByPattern) s.IsChecked = false;
                else if (!kept && s.IsKeptByPattern) s.IsChecked = true;
                s.IsKeptByPattern = kept;
            }
            UpdateUnusedHint();
        }

        private void UpdateUnusedHint()
        {
            UnusedStylesHint = !UsageCounted
                ? (IsRunning ? "Counting line style usage…" : "Line style usage is counted while this tab is open.")
                : UnusedStyles.Count == 0 ? "No unused line styles." : string.Empty;
        }

        // After a committed line-thickness change: refresh the grid, redraw the active view (Revit can
        // leave it stale after an Object Styles change made through the API) and warn when Thin Lines
        // mode hides the new weights.
        private void RefreshAfterThicknessChange(Autodesk.Revit.UI.UIApplication app)
        {
            RefreshAfterChange();

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
