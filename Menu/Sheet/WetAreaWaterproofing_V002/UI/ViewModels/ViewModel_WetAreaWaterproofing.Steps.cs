using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Review;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Run;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.UI.ViewModels
{
    /// <summary>One row of the Review table (tool 2.2.0).</summary>
    public sealed partial class ReviewRowViewModel : ObservableObject
    {
        private readonly Action<ReviewRowViewModel> _changed;

        /// <summary>Wraps <paramref name="item"/>; <paramref name="changed"/> is called when the tick changes.</summary>
        public ReviewRowViewModel(ReviewItem item, bool ticked, Action<ReviewRowViewModel> changed)
        {
            Item = item;
            _isTicked = item.CanTick && ticked;
            _changed = changed;
        }

        /// <summary>The planned item.</summary>
        public ReviewItem Item { get; }
        /// <summary>Group header.</summary>
        public string Room => Item.Room;
        /// <summary>"Roof", "Plan", "Section" or "Note".</summary>
        public string What => Item.Kind.ToString();
        /// <summary>Section target, or empty.</summary>
        public string Target => Item.Target;
        /// <summary>Element cut, slab, or empty.</summary>
        public string CutsThrough => Item.CutsThrough;
        /// <summary>Mode list text, or empty.</summary>
        public string Mode => Item.Mode;
        /// <summary>Drafting view, view made, roof type or plan name.</summary>
        public string PointsTo => Item.PointsTo;
        /// <summary>What happens, in words.</summary>
        public string Happens => Item.Happens;
        /// <summary>"new", "rep", "upd", "keep", "skip" or "err", for the colour of the pill.</summary>
        public string OutcomeKind => Item.Outcome switch
        {
            ReviewOutcome.New => "new",
            ReviewOutcome.Replaces => "rep",
            ReviewOutcome.Updates => "upd",
            ReviewOutcome.Kept => "keep",
            ReviewOutcome.Skipped => "skip",
            _ => "err"
        };
        /// <summary>True when the row can be unticked.</summary>
        public bool CanTick => Item.CanTick;

        /// <summary>The run makes this row.</summary>
        [ObservableProperty] private bool _isTicked;

        partial void OnIsTickedChanged(bool value)
        {
            if (!CanTick && value) { IsTicked = false; return; }
            _changed?.Invoke(this);
        }
    }

    public sealed partial class ViewModel_WetAreaWaterproofing
    {
        /// <summary>Step titles, 1 to 6.</summary>
        public static IReadOnlyList<string> StepTitles { get; } = new[]
        {
            "Find rooms", "Check outlines", "Roof", "Plans and sections", "Review", "Run and report"
        };

        /// <summary>Choices for the Review filter chips, in order.</summary>
        public static IReadOnlyList<string> ReviewFilters { get; } = new[] { "All", "New", "Replaces", "Kept", "Problems" };

        /// <summary>The Review table.</summary>
        public ObservableCollection<ReviewRowViewModel> ReviewRows { get; } = new();
        /// <summary>The Review table as shown: grouped by room, filtered by chip and search.</summary>
        public ICollectionView ReviewView { get; private set; }
        /// <summary>Ticked rooms whose outline did not close (step 2).</summary>
        public ObservableCollection<string> OutlineProblems { get; } = new();

        // Keys unticked in the Review. Kept while the window is open, so going back and returning keeps them.
        private readonly HashSet<string> _unticked = new();

        [ObservableProperty] private int _currentStep = 1;
        [ObservableProperty] private string _stepState1 = "active";
        [ObservableProperty] private string _stepState2 = "pending";
        [ObservableProperty] private string _stepState3 = "pending";
        [ObservableProperty] private string _stepState4 = "pending";
        [ObservableProperty] private string _stepState5 = "pending";
        [ObservableProperty] private string _stepState6 = "pending";
        [ObservableProperty] private bool _step2Skipped;
        [ObservableProperty] private string _step1Summary = string.Empty;
        [ObservableProperty] private string _step2Summary = string.Empty;
        [ObservableProperty] private string _step3Summary = string.Empty;
        [ObservableProperty] private string _step4Summary = string.Empty;
        [ObservableProperty] private string _step5Summary = string.Empty;
        [ObservableProperty] private string _nextButtonText = "Next: Check outlines";
        [ObservableProperty] private string _reviewFilter = "All";
        [ObservableProperty] private string _reviewSearch = string.Empty;
        [ObservableProperty] private string _reviewRoofsText = string.Empty;
        [ObservableProperty] private string _reviewPlansText = string.Empty;
        [ObservableProperty] private string _reviewSectionsText = string.Empty;
        [ObservableProperty] private string _reviewKeptText = string.Empty;
        [ObservableProperty] private string _reviewProblemsText = string.Empty;
        [ObservableProperty] private string _createButtonText = "Create";
        [ObservableProperty] private string _reviewFooterText = string.Empty;

        partial void OnCurrentStepChanged(int value)
        {
            UpdateStepStates();
            NextButtonText = value < 4 ? "Next: " + StepTitles[NextAfter(value) - 1] : "Next: Review";
            NextCommand.NotifyCanExecuteChanged();
            BackCommand.NotifyCanExecuteChanged();
            CreateCommand.NotifyCanExecuteChanged();
        }

        partial void OnStep2SkippedChanged(bool value) => UpdateStepStates();

        /// <summary>
        /// Sets each step's state for the window: "active" (open), "done" (folded, can be changed), "locked"
        /// (folded after the run), "skipped" (step 2 when every outline closed) or "pending" (not reached).
        /// </summary>
        private void UpdateStepStates()
        {
            string State(int n) =>
                n == CurrentStep ? "active"
                : n > CurrentStep ? "pending"
                : n == 2 && Step2Skipped ? "skipped"
                : CurrentStep == 6 ? "locked" : "done";
            StepState1 = State(1);
            StepState2 = State(2);
            StepState3 = State(3);
            StepState4 = State(4);
            StepState5 = State(5);
            StepState6 = State(6);
        }

        partial void OnReviewFilterChanged(string value) => ReviewView?.Refresh();
        partial void OnReviewSearchChanged(string value) => ReviewView?.Refresh();

        /// <summary>Sets up the Review view. Called once, from the constructor.</summary>
        private void InitSteps()
        {
            ReviewView = CollectionViewSource.GetDefaultView(ReviewRows);
            ReviewView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ReviewRowViewModel.Room)));
            ReviewView.Filter = o => o is ReviewRowViewModel r && ReviewRowVisible(r);
        }

        private bool ReviewRowVisible(ReviewRowViewModel r)
        {
            bool chip = ReviewFilter switch
            {
                "New" => r.Item.Outcome == ReviewOutcome.New,
                "Replaces" => r.Item.Outcome is ReviewOutcome.Replaces or ReviewOutcome.Updates,
                "Kept" => r.Item.Outcome == ReviewOutcome.Kept,
                "Problems" => r.Item.Outcome is ReviewOutcome.Problem or ReviewOutcome.Skipped,
                _ => true
            };
            if (!chip) return false;
            string q = (ReviewSearch ?? string.Empty).Trim();
            if (q.Length == 0) return true;
            return new[] { r.Room, r.Target, r.CutsThrough, r.Mode, r.PointsTo, r.Happens, r.What }
                .Any(t => !string.IsNullOrEmpty(t) && t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>True when the step can be opened from its "Change" link (it is behind the current one).</summary>
        public bool CanGoTo(int step) => step >= 1 && step < CurrentStep && !(step == 2 && Step2Skipped) && CurrentStep != 6;

        /// <summary>Reopens an earlier step ("Change").</summary>
        [RelayCommand]
        private void GoToStep(string step)
        {
            if (IsBusy || !int.TryParse(step, out int n) || !CanGoTo(n)) return;
            CurrentStep = n;
        }

        private bool CanNext() => !IsBusy && CurrentStep <= 4 && (CurrentStep != 1 || _ticked.Count > 0);

        /// <summary>Goes to the next step. From step 4 it plans the run and opens the Review.</summary>
        [RelayCommand(CanExecute = nameof(CanNext))]
        private void Next()
        {
            switch (CurrentStep)
            {
                case 1:
                    Step1Summary = BuildStep1Summary();
                    UpdateOutlineProblems();
                    Step2Skipped = OutlineProblems.Count == 0;
                    Step2Summary = Step2Skipped ? "Skipped: every ticked room's outline closed" : $"{OutlineProblems.Count} room(s) need attention";
                    CurrentStep = Step2Skipped ? 3 : 2;
                    break;
                case 2:
                    UpdateOutlineProblems();
                    Step2Summary = OutlineProblems.Count == 0 ? "Every ticked room's outline closed"
                        : $"{OutlineProblems.Count} room(s) still without a closed outline: they are skipped";
                    CurrentStep = 3;
                    break;
                case 3:
                    if (CreateRoofs && string.IsNullOrEmpty(SelectedRoofType))
                    {
                        AddLog(LogLevel.Error, "Pick a roof type first, or untick \"Create roofs\".");
                        return;
                    }
                    Step3Summary = CreateRoofs
                        ? $"{SelectedRoofType}" + (IsWorkshared && !string.IsNullOrEmpty(SelectedWorkset) ? $" · workset {SelectedWorkset}" : string.Empty)
                        : "No roofs";
                    CurrentStep = 4;
                    break;
                case 4:
                    BuildReview();
                    break;
            }
        }

        private bool CanBack() => !IsBusy && CurrentStep is > 1 and < 6;

        /// <summary>Goes back one step (step 2 is passed over when it was skipped).</summary>
        [RelayCommand(CanExecute = nameof(CanBack))]
        private void Back()
        {
            int to = CurrentStep - 1;
            if (to == 2 && Step2Skipped) to = 1;
            CurrentStep = Math.Max(1, to);
        }

        /// <summary>After a run: back to step 1 with the list read again.</summary>
        [RelayCommand]
        private void StartAgain()
        {
            ReviewRows.Clear();
            _unticked.Clear();
            ResultVisible = false;
            CurrentStep = 1;
        }

        /// <summary>Plans the run without changing the model and opens the Review.</summary>
        private void BuildReview()
        {
            if (!CreateRoofs && !CreatePlans && !CreateSections)
            {
                AddLog(LogLevel.Error, "Tick at least one of Roofs, Plans or Sections.");
                return;
            }
            if (!PushSettings()) return;
            Step4Summary = BuildStep4Summary();
            var keys = _ticked.ToList();
            IsBusy = true;
            AddLog(LogLevel.Info, $"Planning the run for {keys.Count} room(s). Nothing is created yet.");
            _session.Preview(keys, _settings, (rows, error) =>
            {
                IsBusy = false;
                if (error != null || rows == null)
                {
                    AddLog(LogLevel.Error, "The run could not be planned: " + (error ?? "no answer from Revit."));
                    return;
                }
                ReviewRows.Clear();
                foreach (ReviewItem item in rows)
                    ReviewRows.Add(new ReviewRowViewModel(item, !_unticked.Contains(item.Key), OnReviewTickChanged));
                ReviewView.Refresh();
                UpdateReviewCounts();
                CurrentStep = 5;
            });
        }

        private void OnReviewTickChanged(ReviewRowViewModel row)
        {
            if (row.IsTicked) _unticked.Remove(row.Item.Key); else _unticked.Add(row.Item.Key);

            // A plan left out takes its sections with it: they sit on that plan.
            if (row.Item.Kind == ReviewKind.Plan)
                foreach (ReviewRowViewModel s in ReviewRows.Where(r => r.Item.RoomKey == row.Item.RoomKey && r.Item.Kind == ReviewKind.Section))
                    if (s.IsTicked != row.IsTicked) s.IsTicked = row.IsTicked;
            UpdateReviewCounts();
        }

        /// <summary>Ticks or unticks every row the filter shows ("Tick all" / "Untick all").</summary>
        [RelayCommand]
        private void TickReview(string on)
        {
            bool tick = on == "1";
            foreach (ReviewRowViewModel r in ReviewView.Cast<ReviewRowViewModel>().Where(r => r.CanTick).ToList())
                r.IsTicked = tick;
        }

        /// <summary>Sets the Review filter chip.</summary>
        [RelayCommand]
        private void SetReviewFilter(string filter) => ReviewFilter = ReviewFilters.Contains(filter) ? filter : "All";

        private bool CanCreate() => !IsBusy && CurrentStep == 5 && ReviewRows.Any(r => r.IsTicked);

        /// <summary>Runs with only the ticked Review rows. One Undo step.</summary>
        [RelayCommand(CanExecute = nameof(CanCreate))]
        private void Create()
        {
            if (!PushSettings()) return;
            HashSet<string> excluded = ReviewBuilder.Excluded(ReviewRows.Select(r => r.Item), _unticked);
            var keys = ReviewRows.Select(r => r.Item.RoomKey).Distinct().Where(k => _ticked.Contains(k)).ToList();
            if (keys.Count == 0) return;
            Step5Summary = $"{ReviewRows.Count(r => r.IsTicked)} of {ReviewRows.Count(r => r.CanTick)} items ticked";
            IsBusy = true;
            AddLog(LogLevel.Info, $"Run started for {keys.Count} room(s), {excluded.Count} item(s) left out.");
            _session.Run(keys, _settings, excluded, OnRunDone);
        }

        /// <summary>Shows the result as step 6.</summary>
        private void ShowResult()
        {
            ResultVisible = true;
            CurrentStep = 6;
        }

        private void UpdateReviewCounts()
        {
            string Count(ReviewKind kind)
            {
                var rows = ReviewRows.Where(r => r.Item.Kind == kind && r.CanTick).ToList();
                int ticked = rows.Count(r => r.IsTicked);
                int replaces = rows.Count(r => r.IsTicked && r.Item.Outcome is ReviewOutcome.Replaces or ReviewOutcome.Updates);
                return rows.Count == 0 ? "none" : $"{ticked} of {rows.Count}" + (replaces > 0 ? $" · {replaces} replace or update" : string.Empty);
            }
            ReviewRoofsText = Count(ReviewKind.Roof);
            ReviewPlansText = Count(ReviewKind.Plan);
            ReviewSectionsText = Count(ReviewKind.Section);
            ReviewKeptText = ReviewRows.Count(r => r.Item.Outcome == ReviewOutcome.Kept).ToString();
            ReviewProblemsText = ReviewRows.Count(r => r.Item.Outcome is ReviewOutcome.Problem or ReviewOutcome.Skipped).ToString();
            int n = ReviewRows.Count(r => r.IsTicked);
            CreateButtonText = n == 1 ? "Create 1 item" : $"Create {n} items";
            ReviewFooterText = $"{n} item(s) ticked · one Undo step";
            CreateCommand.NotifyCanExecuteChanged();
        }

        private void UpdateOutlineProblems()
        {
            OutlineProblems.Clear();
            foreach (RoomCandidate c in _all.Where(c => _ticked.Contains(c.Row.RoomKey)
                && c.Row.Status is RoomStatus.Failed or RoomStatus.NotEnclosed or RoomStatus.Approximate))
                OutlineProblems.Add($"{c.Row.Level} · {c.Row.Number} {c.Row.Name}: {c.Row.StatusDetail}");
        }

        private string BuildStep1Summary()
        {
            var ticked = _all.Where(c => _ticked.Contains(c.Row.RoomKey)).ToList();
            string levels = string.Join(", ", ticked.Select(c => c.Row.Level).Distinct().Take(4));
            int models = ticked.Select(c => c.Row.Source).Distinct().Count();
            return $"{ticked.Count} room(s) ticked · {models} model(s) · {levels}";
        }

        private string BuildStep4Summary()
        {
            var parts = new List<string>();
            if (CreatePlans) parts.Add($"Plans 1:{PlanScaleText}");
            if (CreateSections)
            {
                parts.Add($"{Targets.Count(t => t.On)} section target(s) on");
                parts.Add(OneDraftingViewForAll ? "one drafting view for all" : "one drafting view per target");
            }
            return parts.Count == 0 ? "No plans or sections" : string.Join(" · ", parts);
        }

        private static int NextAfter(int step) => step + 1 > 6 ? 6 : step + 1;
    }
}
