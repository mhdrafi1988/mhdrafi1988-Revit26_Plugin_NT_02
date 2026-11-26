using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Run;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.UI.ViewModels
{
    /// <summary>
    /// Step 4's "Section targets" grid (tool 2.3.0): filter chips and search, the selected row for the
    /// details pane, ticks for "Set for ticked", reordering, and the per-target counts of what the run makes
    /// in the ticked rooms.
    /// </summary>
    public sealed partial class ViewModel_WetAreaWaterproofing
    {
        /// <summary>Filter chips above the grid, in order.</summary>
        public static IReadOnlyList<string> TargetFilters { get; } = new[] { "All", "On", "Problems" };

        /// <summary>Filtered, sortable view over <see cref="Targets"/> for the grid.</summary>
        public ListCollectionView TargetsView { get; private set; }

        [ObservableProperty] private TargetRowViewModel _selectedTarget;
        [ObservableProperty] private string _targetFilter = "All";
        [ObservableProperty] private string _targetSearch = string.Empty;
        [ObservableProperty] private string _bulkLengthText = string.Empty;
        [ObservableProperty] private string _targetsFooterText = string.Empty;
        [ObservableProperty] private string _targetsLimitText = string.Empty;
        [ObservableProperty] private string _targetsTickedText = string.Empty;
        [ObservableProperty] private bool _hasTickedTargets;
        [ObservableProperty] private bool? _allTargetsTicked = false;
        [ObservableProperty] private string _targetChipAllText = "All";
        [ObservableProperty] private string _targetChipOnText = "On";
        [ObservableProperty] private string _targetChipProblemsText = "Problems";

        private bool _settingTicks;

        partial void OnTargetFilterChanged(string value) => RefreshTargetsView();
        partial void OnTargetSearchChanged(string value) => RefreshTargetsView();

        private void RefreshTargetsView()
        {
            if (TargetsView != null && !TargetsView.IsEditingItem && !TargetsView.IsAddingNew) TargetsView.Refresh();
        }
        partial void OnSelectedTargetChanged(TargetRowViewModel value)
        {
            MoveTargetUpCommand.NotifyCanExecuteChanged();
            MoveTargetDownCommand.NotifyCanExecuteChanged();
        }

        /// <summary>The header tick: ticks or unticks every row the filter shows.</summary>
        partial void OnAllTargetsTickedChanged(bool? value)
        {
            if (_settingTicks || value == null) return;
            _settingTicks = true;
            foreach (TargetRowViewModel row in TargetsView.Cast<TargetRowViewModel>()) row.IsTicked = value.Value;
            _settingTicks = false;
            UpdateTickedTargets();
        }

        private void InitTargetGrid()
        {
            TargetsView = new ListCollectionView(Targets) { Filter = o => o is TargetRowViewModel row && ShowTarget(row) };
            RenumberTargets();
            _selectedTarget = Targets.FirstOrDefault();
            UpdateTickedTargets();
        }

        private bool ShowTarget(TargetRowViewModel row)
        {
            if (TargetFilter == "On" && !row.On) return false;
            if (TargetFilter == "Problems" && !row.HasProblem) return false;
            string q = (TargetSearch ?? string.Empty).Trim();
            if (q.Length == 0) return true;
            return Contains(row.Label, q) || Contains(row.SelectedMode, q) || Contains(row.PointsToName, q)
                || Contains(row.PointsToReference, q) || Contains(row.StatusText, q);
        }

        private static bool Contains(string text, string q) => (text ?? string.Empty).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;

        /// <summary>Sets the filter chip.</summary>
        [RelayCommand]
        private void SetTargetFilter(string filter) => TargetFilter = TargetFilters.Contains(filter) ? filter : "All";

        private void OnTargetTickChanged(TargetRowViewModel row)
        {
            if (!_settingTicks) UpdateTickedTargets();
        }

        private void UpdateTickedTargets()
        {
            int n = Targets.Count(t => t.IsTicked);
            HasTickedTargets = n > 0;
            TargetsTickedText = n == 1 ? "1 ticked" : $"{n} ticked";
            _settingTicks = true;
            AllTargetsTicked = n == 0 ? false : n == Targets.Count ? true : null;
            _settingTicks = false;
        }

        private List<TargetRowViewModel> TickedTargets() => Targets.Where(t => t.IsTicked).ToList();

        /// <summary>"Set for ticked": mode.</summary>
        public void BulkSetMode(string mode)
        {
            if (!SectionModes.Labels.Contains(mode)) return;
            foreach (TargetRowViewModel row in TickedTargets()) row.SelectedMode = mode;
        }

        /// <summary>"Set for ticked": per item (only rows that can have several matches).</summary>
        public void BulkSetPerItem(string perItem)
        {
            if (!TargetRowViewModel.PerItemChoices.Contains(perItem)) return;
            foreach (TargetRowViewModel row in TickedTargets().Where(r => r.CanHaveMany)) row.PerItem = perItem;
        }

        /// <summary>"Set for ticked": length, mm.</summary>
        [RelayCommand]
        private void BulkSetLength()
        {
            if (!Number(BulkLengthText, "Length", 1, 20000, out double length)) return;
            string text = length.ToString("0.#", CultureInfo.CurrentCulture);
            foreach (TargetRowViewModel row in TickedTargets()) row.LengthText = text;
        }

        /// <summary>"Set for ticked": turn on (parameter "1") or off.</summary>
        [RelayCommand]
        private void BulkSetOn(string on)
        {
            foreach (TargetRowViewModel row in TickedTargets()) row.On = on == "1";
        }

        /// <summary>Unticks every row.</summary>
        [RelayCommand]
        private void ClearTargetTicks()
        {
            _settingTicks = true;
            foreach (TargetRowViewModel row in Targets) row.IsTicked = false;
            _settingTicks = false;
            UpdateTickedTargets();
        }

        private bool CanMoveTargetUp() => SelectedTarget != null && Targets.IndexOf(SelectedTarget) > 0;
        private bool CanMoveTargetDown() => SelectedTarget != null && Targets.IndexOf(SelectedTarget) < Targets.Count - 1;

        /// <summary>Moves the selected target one place up (higher rows win at the six-per-room limit).</summary>
        [RelayCommand(CanExecute = nameof(CanMoveTargetUp))]
        private void MoveTargetUp() => MoveTarget(SelectedTarget, -1);

        /// <summary>Moves the selected target one place down.</summary>
        [RelayCommand(CanExecute = nameof(CanMoveTargetDown))]
        private void MoveTargetDown() => MoveTarget(SelectedTarget, +1);

        /// <summary>Moves <paramref name="row"/> to place <paramref name="index"/> (0-based), for drag and drop.</summary>
        public void MoveTargetTo(TargetRowViewModel row, int index)
        {
            int from = row == null ? -1 : Targets.IndexOf(row);
            if (from < 0) return;
            index = Math.Clamp(index, 0, Targets.Count - 1);
            if (index == from) return;
            Targets.Move(from, index);
            SelectedTarget = row;
            OnTargetsChanged();
        }

        /// <summary>Puts the targets back to the tool's defaults (order, on/off, lengths, drafting view names).</summary>
        public void ResetTargets()
        {
            Targets.Clear();
            foreach (SectionTargetSetting target in SectionTargets.Defaults())
                Targets.Add(new TargetRowViewModel(target, OnTargetsChanged) { TickChanged = OnTargetTickChanged });
            SelectedTarget = Targets.FirstOrDefault();
            UpdateTickedTargets();
            OnTargetsChanged();
            AddLog(Shared.Models.LogLevel.Info, "Section targets put back to the defaults.");
        }

        /// <summary>Opens "Assign drafting views" on the selected target's slot.</summary>
        [RelayCommand]
        private void AssignSelectedTarget()
        {
            OpenAssign();
            if (!OneDraftingViewForAll && SelectedTarget != null)
            {
                AssignSlotViewModel slot = AssignSlots.FirstOrDefault(s => s.Title == SelectedTarget.Label);
                if (slot?.CanAssign == true) SelectedSlot = slot;
            }
        }

        /// <summary>Priorities, the drafting view each row points to, the counts, the chips and the footer.</summary>
        private void UpdateTargetGrid()
        {
            if (TargetsView == null) return; // still loading
            RenumberTargets();
            UpdatePointsTo();
            UpdateTargetCoverage();
        }

        private void RenumberTargets()
        {
            for (int i = 0; i < Targets.Count; i++) Targets[i].SetPriority(i + 1, Targets.Count);
            MoveTargetUpCommand.NotifyCanExecuteChanged();
            MoveTargetDownCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Same choice as the run's section planner: own view, else the shared one, else the default name.</summary>
        private void UpdatePointsTo()
        {
            if (TargetsView == null) return;
            foreach (TargetRowViewModel row in Targets)
            {
                string name = !row.UsesDraftingView ? string.Empty
                    : OneDraftingViewForAll || string.IsNullOrWhiteSpace(row.DraftingViewName)
                        ? (string.IsNullOrWhiteSpace(SharedDraftingViewName) ? SectionTargets.DefaultDraftingName(row.Kind) : SharedDraftingViewName.Trim())
                        : row.DraftingViewName.Trim();
                row.SetPointsTo(name, DraftingViewItems.ToList());
            }
        }

        /// <summary>
        /// Counts, for the ticked rooms the run can do, what each target finds and makes. Uses the rooms as last
        /// read from the model (Refresh reads them again); the Review shows the exact list.
        /// </summary>
        private void UpdateTargetCoverage()
        {
            if (TargetsView == null) return;
            try
            {
                var ticked = _all.Where(c => _ticked.Contains(c.Row.RoomKey)).ToList();
                var jobs = RunPlanner.Plan(ticked, _settings, "preview").Jobs.ToDictionary(j => j.RoomKey);
                var rooms = ticked.Select(c => new CoverageRoom($"{c.Row.Level} · {c.Row.Number} {c.Row.Name}",
                    jobs.TryGetValue(c.Row.RoomKey, out RoofJob job) ? new RoomSectionInput(job.Number, job.Outer, c.Inside, c.Items) : null)).ToList();

                var settings = Targets.Select(row => row.ToSetting(out _) ?? new SectionTargetSetting
                {
                    Kind = row.Kind, On = row.On, Mode = row.Mode, LengthMm = SectionTargets.DefaultLengthMm(row.Kind),
                    AllMatches = row.CanHaveMany && row.PerItem == TargetRowViewModel.PerItemChoices[1]
                }).ToList();
                IReadOnlyList<TargetCoverageRow> counts = CreateSections ? TargetCoverage.Count(rooms, settings) : Array.Empty<TargetCoverageRow>();
                for (int i = 0; i < Targets.Count; i++) Targets[i].SetCoverage(i < counts.Count ? counts[i] : null);

                int usable = rooms.Count(r => r.Input != null);
                int sections = Targets.Where(t => t.On).Sum(t => t.Makes);
                int atLimit = counts.SelectMany(c => c.DroppedRooms).Distinct().Count();
                TargetsFooterText = !CreateSections ? "Create sections is off: no section is placed."
                    : $"{Targets.Count(t => t.On)} of {Targets.Count} targets on · {sections} section(s) in {usable} room(s)";
                TargetsLimitText = atLimit == 0 ? string.Empty
                    : $"{atLimit} room(s) hit the {SectionTargets.MaxPerRoom}-section limit: lower rows are dropped there";
            }
            catch (Exception ex)
            {
                // The counts are a guide only: never block the step on them.
                foreach (TargetRowViewModel row in Targets) row.SetCoverage(null);
                TargetsFooterText = "The counts could not be worked out: " + ex.Message;
                TargetsLimitText = string.Empty;
            }
            TargetChipAllText = $"All {Targets.Count}";
            TargetChipOnText = $"On {Targets.Count(t => t.On)}";
            TargetChipProblemsText = $"Problems {Targets.Count(t => t.HasProblem)}";
            if (!TargetsView.IsEditingItem && !TargetsView.IsAddingNew) TargetsView.Refresh();
        }
    }
}
