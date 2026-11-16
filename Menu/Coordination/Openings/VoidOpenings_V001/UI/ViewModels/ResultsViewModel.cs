using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>Which rows the RESULTS table shows.</summary>
    public enum ResultFilter
    {
        /// <summary>Every row.</summary>
        All,
        /// <summary>Ticked rows.</summary>
        Approved,
        /// <summary>Rows that are neither ticked nor rejected.</summary>
        NotApproved,
        /// <summary>Rows that need a review.</summary>
        Review,
        /// <summary>Rejected rows.</summary>
        Rejected,
    }

    /// <summary>
    /// The RESULTS tab: the clashes of the last Analyze with the opening sized for each, the
    /// approve / reject decisions, and zoom / highlight in Revit.
    /// </summary>
    public sealed class ResultsViewModel : ObservableObject
    {
        private readonly NavigationService _navigation;
        private readonly Action<string> _showError;
        private readonly List<ClashRowViewModel> _all = new List<ClashRowViewModel>();
        private IReadOnlyList<ClashRowViewModel> _selection = new List<ClashRowViewModel>();
        private string _findText = string.Empty;
        private ResultFilter _filter = ResultFilter.All;
        private string _summary = "Not analysed yet";
        private string _emptyText = "Press ANALYZE to find where the MEP elements pass through the structure.";
        private string _notAnalysedSummary = string.Empty;
        private string _notAnalysedText = string.Empty;
        private bool _hasRun;

        /// <summary>Creates the tab.</summary>
        /// <param name="navigation">Zooms and selects in Revit.</param>
        /// <param name="showError">Shows an error in the window's notification strip.</param>
        public ResultsViewModel(NavigationService navigation, Action<string> showError)
        {
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _showError = showError;
            ZoomCommand = new AsyncRelayCommand(() => NavigateAsync(_navigation.ZoomToAsync), HasSelection);
            HighlightMepCommand = new AsyncRelayCommand(() => NavigateAsync(_navigation.HighlightMepAsync), HasSelection);
            HighlightStructureCommand = new AsyncRelayCommand(() => NavigateAsync(_navigation.HighlightStructureAsync), HasSelection);
            ApproveCommand = new RelayCommand(() => Decide(row => row.Approve()), HasSelection);
            RejectCommand = new RelayCommand(() => Decide(row => row.Reject()), HasSelection);
        }

        /// <summary>The rows shown, after Find and Show.</summary>
        public ObservableCollection<ClashRowViewModel> Rows { get; } = new ObservableCollection<ClashRowViewModel>();

        /// <summary>Choices of the Show list.</summary>
        public IReadOnlyList<ChoiceItem<ResultFilter>> FilterOptions { get; } = new[]
        {
            new ChoiceItem<ResultFilter>(ResultFilter.All, "All"),
            new ChoiceItem<ResultFilter>(ResultFilter.Approved, "Approved"),
            new ChoiceItem<ResultFilter>(ResultFilter.NotApproved, "Not approved"),
            new ChoiceItem<ResultFilter>(ResultFilter.Review, "To review"),
            new ChoiceItem<ResultFilter>(ResultFilter.Rejected, "Rejected"),
        };

        /// <summary>Zooms the active Revit view to the selected row's opening.</summary>
        public IAsyncRelayCommand ZoomCommand { get; }

        /// <summary>Selects the selected row's MEP element in Revit.</summary>
        public IAsyncRelayCommand HighlightMepCommand { get; }

        /// <summary>Selects the selected row's structural element in Revit.</summary>
        public IAsyncRelayCommand HighlightStructureCommand { get; }

        /// <summary>Approves the selected rows; rejected ones come back.</summary>
        public IRelayCommand ApproveCommand { get; }

        /// <summary>Rejects the selected rows.</summary>
        public IRelayCommand RejectCommand { get; }

        /// <summary>Text the rows must contain: a clash id, an element id, a model, a category or a note.</summary>
        public string FindText
        {
            get => _findText;
            set { if (SetProperty(ref _findText, value ?? string.Empty)) ApplyFilter(); }
        }

        /// <summary>Which rows are shown.</summary>
        public ResultFilter Filter
        {
            get => _filter;
            set { if (SetProperty(ref _filter, value)) ApplyFilter(); }
        }

        /// <summary>Counts shown in the card header: "7 clashes · 5 approved · 1 rejected · 1 to review".</summary>
        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        /// <summary>Short text for the Results tab button: the number of clashes, or empty before the first run.</summary>
        public string TabCount => _hasRun ? $" ({_all.Count:N0})" : string.Empty;

        /// <summary>True when no row is shown; the table then shows <see cref="EmptyText"/>.</summary>
        public bool IsEmpty => Rows.Count == 0;

        /// <summary>What the empty table says.</summary>
        public string EmptyText
        {
            get => _emptyText;
            private set => SetProperty(ref _emptyText, value);
        }

        /// <summary>"3 elements could not be analysed", or empty.</summary>
        public string NotAnalysedSummary
        {
            get => _notAnalysedSummary;
            private set
            {
                if (SetProperty(ref _notAnalysedSummary, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasNotAnalysed));
            }
        }

        /// <summary>True when the last run left elements out.</summary>
        public bool HasNotAnalysed => _notAnalysedSummary.Length > 0;

        /// <summary>The elements left out, one per line (model, category, element id, reason), for Copy list.</summary>
        public string NotAnalysedText
        {
            get => _notAnalysedText;
            private set => SetProperty(ref _notAnalysedText, value ?? string.Empty);
        }

        /// <summary>Every row of the last run, shown or not.</summary>
        public IReadOnlyList<ClashRowViewModel> AllRows => _all;

        /// <summary>The approved openings, for Generate.</summary>
        public IReadOnlyList<OpeningData> ApprovedOpenings => _all.Where(r => r.IsApproved).Select(r => r.Review.Opening).ToList();

        /// <summary>
        /// Shows the result of an Analyze run. Decisions made on clashes that are still there are kept.
        /// </summary>
        public void Show(AnalysisResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            IReadOnlyList<ReviewedOpening> reviewed = ReviewedOpening.From(result.Openings, _all.Select(r => r.Review));
            _all.Clear();
            _all.AddRange(reviewed.Select(r => new ClashRowViewModel(r, OnDecisionChanged)));
            _hasRun = true;

            int skipped = result.NotAnalysed.Count;
            NotAnalysedSummary = skipped == 0 ? string.Empty
                : skipped == 1 ? "1 element could not be analysed."
                : $"{skipped:N0} elements could not be analysed.";
            NotAnalysedText = NotAnalysedList(result.NotAnalysed);

            OnPropertyChanged(nameof(TabCount));
            ApplyFilter();
        }

        /// <summary>Tells the tab which rows are selected in the table (the table allows several).</summary>
        public void SetSelection(IReadOnlyList<ClashRowViewModel> rows)
        {
            _selection = rows ?? new List<ClashRowViewModel>();
            ZoomCommand.NotifyCanExecuteChanged();
            HighlightMepCommand.NotifyCanExecuteChanged();
            HighlightStructureCommand.NotifyCanExecuteChanged();
            ApproveCommand.NotifyCanExecuteChanged();
            RejectCommand.NotifyCanExecuteChanged();
        }

        private bool HasSelection() => _selection.Count > 0;

        private async Task NavigateAsync(Func<OpeningData, Task> action)
        {
            ClashRowViewModel row = _selection.FirstOrDefault();
            if (row == null)
                return;
            try
            {
                await action(row.Review.Opening);
            }
            catch (Exception ex)
            {
                _showError?.Invoke(ex.Message);
            }
        }

        private void Decide(Action<ClashRowViewModel> decision)
        {
            foreach (ClashRowViewModel row in _selection.ToList())
                decision(row);
        }

        // A decision changes the counts but not which rows are listed: a row must not vanish from
        // under the cursor because its status left the current filter.
        private void OnDecisionChanged() => UpdateSummary();

        private void ApplyFilter()
        {
            Rows.Clear();
            foreach (ClashRowViewModel row in _all.Where(Shown))
                Rows.Add(row);

            EmptyText = !_hasRun ? "Press ANALYZE to find where the MEP elements pass through the structure."
                : _all.Count == 0 ? "No clashes found: no selected MEP element passes through a selected structural element."
                : "No row matches Find and Show.";
            OnPropertyChanged(nameof(IsEmpty));
            UpdateSummary();
        }

        private bool Shown(ClashRowViewModel row)
        {
            if (!row.Matches(_findText))
                return false;
            switch (_filter)
            {
                case ResultFilter.Approved: return row.IsApproved;
                case ResultFilter.NotApproved: return !row.IsApproved && !row.IsRejected;
                case ResultFilter.Review: return row.IsReview;
                case ResultFilter.Rejected: return row.IsRejected;
                default: return true;
            }
        }

        private void UpdateSummary()
        {
            if (!_hasRun)
            {
                Summary = "Not analysed yet";
                return;
            }
            var parts = new List<string>
            {
                _all.Count == 1 ? "1 clash" : $"{_all.Count:N0} clashes",
                $"{_all.Count(r => r.IsApproved):N0} approved",
                $"{_all.Count(r => r.IsRejected):N0} rejected",
                $"{_all.Count(r => r.IsReview && !r.IsApproved):N0} to review",
            };
            if (Rows.Count != _all.Count)
                parts.Add($"{Rows.Count:N0} shown");
            Summary = string.Join(" · ", parts);
        }

        private static string NotAnalysedList(IReadOnlyList<SkippedElement> skipped)
        {
            var text = new StringBuilder();
            if (skipped.Count > 0)
                text.AppendLine("Model\tCategory\tElement id\tReason");
            foreach (SkippedElement element in skipped)
                text.AppendLine($"{element.ModelName}\t{element.Category}\t{element.ElementId}\t{element.Reason}");
            return text.ToString();
        }
    }
}
