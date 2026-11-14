using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.UI.ViewModels
{
    /// <summary>One view (or the sheet itself) in the Views popover.</summary>
    public partial class ViewRowViewModel : ObservableObject
    {
        private readonly Action _checkedChanged;

        /// <summary>Creates the row for <paramref name="info"/>; <paramref name="checkedChanged"/> runs when the tick changes.</summary>
        public ViewRowViewModel(ViewInfo info, bool isChecked, Action checkedChanged)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            IsChecked = HasNotes && isChecked;
            _checkedChanged = checkedChanged; // set last: the initial tick is not a change
        }

        /// <summary>The view as read from the model.</summary>
        public ViewInfo Info { get; }

        /// <summary>View element id (the sheet id for the sheet itself).</summary>
        public long Key => Info.Key;

        /// <summary>View name.</summary>
        public string Name => Info.Name;

        /// <summary>View type label.</summary>
        public string TypeLabel => Info.TypeLabel;

        /// <summary>"1 : 100" or "—".</summary>
        public string ScaleText => Info.ScaleText;

        /// <summary>Number of text notes shown in the view.</summary>
        public int NoteCount => Info.Notes.Count;

        /// <summary>False for views without text notes: shown greyed out and cannot be ticked.</summary>
        public bool HasNotes => Info.Notes.Count > 0;

        /// <summary>True when another user owns the view (lock icon).</summary>
        public bool IsLocked => Info.Owner != null;

        /// <summary>Lock icon tooltip.</summary>
        public string LockTip => IsLocked ? "Owned by " + Info.Owner + ": its notes will be skipped." : null;

        /// <summary>True when the view is also on other sheets (legends).</summary>
        public bool IsShared => Info.AlsoOnSheets.Count > 0;

        /// <summary>Shared icon tooltip.</summary>
        public string SharedTip => IsShared ? "Also on " + string.Join(", ", Info.AlsoOnSheets) + ": a change shows there too." : null;

        /// <summary>Row tooltip.</summary>
        public string RowTip => HasNotes ? null : "No text notes in this view.";

        /// <summary>Hidden by the popover's filter box.</summary>
        [ObservableProperty] private bool isVisible = true;

        /// <summary>Ticked: its notes are searched.</summary>
        [ObservableProperty] private bool isChecked;

        partial void OnIsCheckedChanged(bool value) => _checkedChanged?.Invoke();
    }

    /// <summary>
    /// A view-type group in the Views popover ("Section", "Legend"…): header tick box selects or clears
    /// every view of that type, and the group can be collapsed.
    /// </summary>
    public partial class ViewTypeGroupViewModel : ObservableObject
    {
        private readonly Action<Action> _runBatched;

        /// <summary>
        /// Creates the group. <paramref name="runBatched"/> runs several tick changes as one update
        /// (the preview is rebuilt once, not once per view).
        /// </summary>
        public ViewTypeGroupViewModel(string typeLabel, IEnumerable<ViewRowViewModel> views, Action<Action> runBatched)
        {
            TypeLabel = typeLabel;
            _runBatched = runBatched ?? (a => a());
            foreach (var v in views) Views.Add(v);
        }

        /// <summary>The view type.</summary>
        public string TypeLabel { get; }

        /// <summary>The group's views, in sheet order.</summary>
        public ObservableCollection<ViewRowViewModel> Views { get; } = new();

        /// <summary>Views with text notes (the ones that can be ticked).</summary>
        public int SelectableCount => Views.Count(v => v.HasNotes);

        /// <summary>Ticked views.</summary>
        public int CheckedCount => Views.Count(v => v.IsChecked);

        /// <summary>True when at least one view is ticked (the group shows as a chip under the button).</summary>
        public bool HasChecked => CheckedCount > 0;

        /// <summary>False when no view of the type has text notes (header greyed out).</summary>
        public bool CanCheck => SelectableCount > 0;

        /// <summary>"2/3 checked · 6 notes".</summary>
        public string Summary => $"{CheckedCount}/{SelectableCount} checked · {Views.Sum(v => v.NoteCount)} notes";

        /// <summary>"Section 2/3", for the type buttons and chips.</summary>
        public string PillText => $"{TypeLabel} {CheckedCount}/{SelectableCount}";

        /// <summary>True when every selectable view is ticked (type button shown as on).</summary>
        public bool IsAllChecked => CanCheck && CheckedCount == SelectableCount;

        /// <summary>True when some but not all are ticked.</summary>
        public bool IsPartChecked => CheckedCount > 0 && CheckedCount < SelectableCount;

        /// <summary>Header tick box: true = all, false = none, null = some. Setting it ticks or clears all.</summary>
        public bool? IsChecked
        {
            get => IsAllChecked ? true : CheckedCount == 0 ? false : null;
            set => SetAll(value != false);
        }

        /// <summary>Group expanded in the popover.</summary>
        [ObservableProperty] private bool isExpanded = true;

        /// <summary>False when the filter hides every view of the group.</summary>
        [ObservableProperty] private bool isVisible = true;

        /// <summary>Type button: ticks every view of the type, or clears them when all are ticked.</summary>
        [RelayCommand]
        private void Toggle() => SetAll(!IsAllChecked);

        /// <summary>Ticks or clears every view of the type that has text notes.</summary>
        public void SetAll(bool value)
        {
            _runBatched(() =>
            {
                foreach (var v in Views.Where(v => v.HasNotes))
                    v.IsChecked = value;
            });
            Refresh();
        }

        /// <summary>Re-reads the counts after a tick changed.</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(CheckedCount));
            OnPropertyChanged(nameof(HasChecked));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(PillText));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(IsPartChecked));
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    /// <summary>State of a preview row.</summary>
    public enum RowStatus
    {
        /// <summary>Ticked and will change.</summary>
        WillChange,

        /// <summary>Ticked and will change; the view is also on other sheets.</summary>
        WillChangeShared,

        /// <summary>Unticked by the user.</summary>
        Excluded,

        /// <summary>Cannot change (owned, in a group, read-only, no change…).</summary>
        Blocked,

        /// <summary>After Apply: changed.</summary>
        Changed,

        /// <summary>After Apply: skipped.</summary>
        Skipped,

        /// <summary>After Apply: Revit refused the change.</summary>
        Failed
    }

    /// <summary>One matching text note in the preview grid.</summary>
    public partial class PreviewRowViewModel : ObservableObject
    {
        private readonly Action<PreviewRowViewModel> _includedChanged;

        /// <summary>Creates the row.</summary>
        public PreviewRowViewModel(ViewInfo view, NoteInfo note, ReplaceOutcome outcome, string blockReason,
            bool isIncluded, Action<PreviewRowViewModel> includedChanged)
        {
            View = view;
            Note = note;
            Outcome = outcome;
            BlockReason = blockReason;
            IsIncluded = CanApply && isIncluded;
            RefreshStatus();
            _includedChanged = includedChanged; // set last: the initial tick is not a change
        }

        /// <summary>The view the note is shown in.</summary>
        public ViewInfo View { get; }

        /// <summary>The note as read.</summary>
        public NoteInfo Note { get; }

        /// <summary>The replace result for the note.</summary>
        public ReplaceOutcome Outcome { get; }

        /// <summary>Why the note cannot change, or null.</summary>
        public string BlockReason { get; }

        /// <summary>Element id of the note.</summary>
        public long NoteId => Note.Id;

        /// <summary>View name.</summary>
        public string ViewName => View.Name;

        /// <summary>View type.</summary>
        public string ViewType => View.TypeLabel;

        /// <summary>True when the view is owned by another user.</summary>
        public bool IsLocked => View.Owner != null;

        /// <summary>True when the view is also on other sheets.</summary>
        public bool IsShared => View.AlsoOnSheets.Count > 0;

        /// <summary>"A-301, A-302".</summary>
        public string AlsoOnText => string.Join(", ", View.AlsoOnSheets);

        /// <summary>Current text, with the found parts marked.</summary>
        public IReadOnlyList<TextSegment> OldSegments => Outcome.OldSegments;

        /// <summary>New text, with the added parts marked.</summary>
        public IReadOnlyList<TextSegment> NewSegments => Outcome.NewSegments;

        /// <summary>"Bold kept" tag, or null for plain notes.</summary>
        public string FormattingText => Note.Formatting == null ? null : Note.Formatting + " kept";

        /// <summary>True when the note has formatting.</summary>
        public bool HasFormatting => Note.Formatting != null;

        /// <summary>True when the note can be changed.</summary>
        public bool CanApply => BlockReason == null;

        /// <summary>Tick box enabled: before Apply and changeable.</summary>
        public bool IsEditable => CanApply && !_isResult;

        /// <summary>Ticked: the note is changed by Apply.</summary>
        [ObservableProperty] private bool isIncluded;

        /// <summary>Row state.</summary>
        [ObservableProperty] private RowStatus status;

        /// <summary>Status badge text.</summary>
        [ObservableProperty] private string statusText = string.Empty;

        private bool _isResult;

        partial void OnIsIncludedChanged(bool value)
        {
            RefreshStatus();
            _includedChanged?.Invoke(this);
        }

        /// <summary>Recomputes the badge from the tick (before Apply).</summary>
        public void RefreshStatus()
        {
            if (_isResult) return;
            if (!CanApply)
            {
                Status = RowStatus.Blocked;
                StatusText = BlockReason;
            }
            else if (!IsIncluded)
            {
                Status = RowStatus.Excluded;
                StatusText = IsShared ? "Excluded · also on " + AlsoOnText : "Excluded";
            }
            else
            {
                Status = IsShared ? RowStatus.WillChangeShared : RowStatus.WillChange;
                StatusText = IsShared ? "Will change · also on " + AlsoOnText : "Will change";
            }
        }

        /// <summary>Turns the row into an Apply result row (tick box disabled).</summary>
        public void SetResult(RowStatus result, string text)
        {
            _isResult = true;
            Status = result;
            StatusText = text;
            OnPropertyChanged(nameof(IsEditable));
        }

        /// <summary>The change to send to Revit.</summary>
        public NoteChange ToChange() => new()
        {
            NoteId = Note.Id,
            ViewName = View.Name,
            ExpectedText = Note.Text,
            NewText = Outcome.NewText,
            EditPasses = Outcome.EditPasses
        };
    }

    /// <summary>One line under the AFTER cards (a skipped or failed note).</summary>
    public sealed record ResultLine(string Text, bool IsFailure);
}
