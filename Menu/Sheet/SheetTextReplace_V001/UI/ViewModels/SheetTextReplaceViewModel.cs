using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Services;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.UI.ViewModels
{
    /// <summary>
    /// Window state for Sheet Text Replace. Keeps the latest <see cref="SheetContent"/> and rebuilds
    /// the preview in memory on every change to the inputs, options or ticked views (the summary then
    /// goes back to BEFORE). All Revit access goes through <see cref="ISheetTextSession"/>.
    /// </summary>
    public partial class SheetTextReplaceViewModel : ToolViewModelBase, IDisposable
    {
        private const string NotAccepted = "Revit did not accept the request. Close any open dialog in Revit and try again.";

        /// <summary>Order of the view-type groups; other types follow alphabetically.</summary>
        private static readonly string[] TypeOrder =
        {
            ViewInfo.SheetItselfType, "Floor Plan", "Ceiling Plan", "Structural Plan", "Area Plan",
            "Elevation", "Section", "Detail", "Drafting", "Legend", "3D"
        };

        private readonly ISheetTextSession _session;
        private readonly ListCollectionView _sheetsView;
        private readonly HashSet<long> _excluded = new();
        private readonly List<ViewRowViewModel> _viewRows = new();
        private SheetContent _content;
        private bool _suspend;
        private bool _bulkRows;
        private int _viewBatch;
        private bool _disposed;

        /// <summary>Creates the view model; preselects the session's initial sheet when there is one.</summary>
        public SheetTextReplaceViewModel(ISheetTextSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            foreach (var sheet in session.Sheets)
                Sheets.Add(sheet);
            _sheetsView = new ListCollectionView(Sheets) { Filter = FilterSheet };

            AddLog(LogLevel.Info, $"{ToolCatalog.SheetTextReplace.Title} opened. {Sheets.Count} sheet(s) in the model.");
            if (session.Initial != null)
            {
                SheetHint = "Preselected: the active view is this sheet.";
                SetContent(session.Initial, resetChecks: true, recompute: true);
                LogSheetLoaded(session.Initial);
            }
            else
            {
                SheetHint = "The active view is not a sheet: pick one.";
                Recompute();
            }
        }

        // ── Sheet picker ─────────────────────────────────────────────────────

        /// <summary>All sheets of the model.</summary>
        public ObservableCollection<SheetItem> Sheets { get; } = new();

        /// <summary>Sheets filtered by <see cref="SheetSearchText"/>.</summary>
        public ICollectionView SheetsView => _sheetsView;

        /// <summary>Search box of the sheet picker (number or name, anywhere in the label).</summary>
        [ObservableProperty] private string sheetSearchText = string.Empty;

        /// <summary>Line under the sheet picker.</summary>
        [ObservableProperty] private string sheetHint = string.Empty;

        /// <summary>The sheet shown, or null before one is picked.</summary>
        public SheetItem CurrentSheet => _content?.Sheet;

        /// <summary>Text on the sheet picker button.</summary>
        public string CurrentSheetLabel => CurrentSheet?.Label ?? "Pick a sheet…";

        /// <summary>True once a sheet is loaded.</summary>
        public bool HasSheet => _content != null;

        partial void OnSheetSearchTextChanged(string value) => _sheetsView.Refresh();

        private bool FilterSheet(object item)
            => string.IsNullOrWhiteSpace(SheetSearchText)
               || (item is SheetItem s && s.Label.Contains(SheetSearchText.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Loads <paramref name="sheet"/> (picked in the sheet list).</summary>
        [RelayCommand]
        public void PickSheet(SheetItem sheet)
        {
            if (sheet == null || IsRunning || sheet.Id == CurrentSheet?.Id) return;
            SetBusy(true, $"Reading {sheet.Label}…");
            bool accepted = _session.Load(sheet.Id, result =>
            {
                SetBusy(false, string.Empty);
                if (result.Content == null)
                {
                    AddLog(LogLevel.Error, result.Error ?? "The sheet could not be read.");
                    return;
                }
                SheetHint = "Picked by you.";
                SetContent(result.Content, resetChecks: true, recompute: true);
                LogSheetLoaded(result.Content);
            });
            if (!accepted)
            {
                SetBusy(false, string.Empty);
                AddLog(LogLevel.Error, NotAccepted);
            }
        }

        /// <summary>Re-reads the sheet from the model (after changes made in Revit).</summary>
        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private void Refresh()
        {
            var sheet = CurrentSheet;
            SetBusy(true, "Re-reading the sheet…");
            bool accepted = _session.Load(sheet.Id, result =>
            {
                SetBusy(false, string.Empty);
                if (result.Content == null)
                {
                    AddLog(LogLevel.Error, result.Error ?? "The sheet could not be read.");
                    return;
                }

                var before = AllNotes(_content).ToDictionary(n => n.Id, n => n.Text);
                var after = AllNotes(result.Content).ToList();
                int changed = after.Count(n => before.TryGetValue(n.Id, out var t) && t != n.Text);
                int added = after.Count(n => !before.ContainsKey(n.Id));
                int removed = before.Count - (after.Count - added);

                SetContent(result.Content, resetChecks: false, recompute: true);
                string what = changed + added + removed == 0
                    ? "Nothing changed since the last read."
                    : $"Since the last read: {changed} note(s) changed, {added} added, {removed} removed. The preview shows the current text.";
                AddLog(changed + added + removed == 0 ? LogLevel.Info : LogLevel.Warning,
                    $"Re-read {result.Content.Sheet.Label}: {result.Content.Views.Count} view(s), {after.Count} text note(s). {what}");
            });
            if (!accepted)
            {
                SetBusy(false, string.Empty);
                AddLog(LogLevel.Error, NotAccepted);
            }
        }

        private bool CanRefresh() => HasSheet && !IsRunning;

        // ── Views popover ───────────────────────────────────────────────────

        /// <summary>The sheet's views grouped by view type.</summary>
        public ObservableCollection<ViewTypeGroupViewModel> Groups { get; } = new();

        /// <summary>"Views… (3/5) ▾".</summary>
        public string ViewsButtonText => $"Views… ({_viewRows.Count(v => v.IsChecked)}/{_viewRows.Count}) ▾";

        /// <summary>Filter box in the Views popover.</summary>
        [ObservableProperty] private string viewFilterText = string.Empty;

        partial void OnViewFilterTextChanged(string value) => ApplyViewFilter();

        /// <summary>Ticks every shown view that has text notes.</summary>
        [RelayCommand]
        private void SelectAllViews() => RunBatched(() =>
        {
            foreach (var v in _viewRows.Where(v => v.IsVisible && v.HasNotes)) v.IsChecked = true;
        });

        /// <summary>Clears every shown view.</summary>
        [RelayCommand]
        private void SelectNoViews() => RunBatched(() =>
        {
            foreach (var v in _viewRows.Where(v => v.IsVisible)) v.IsChecked = false;
        });

        private void RunBatched(Action action)
        {
            _viewBatch++;
            try { action(); }
            finally { _viewBatch--; }
            OnViewCheckedChanged();
        }

        private void OnViewCheckedChanged()
        {
            if (_suspend || _viewBatch > 0) return;
            foreach (var g in Groups) g.Refresh();
            OnPropertyChanged(nameof(ViewsButtonText));
            Recompute();
        }

        private void ApplyViewFilter()
        {
            string q = (ViewFilterText ?? string.Empty).Trim();
            foreach (var v in _viewRows)
                v.IsVisible = q.Length == 0 || (v.Name + " " + v.TypeLabel).Contains(q, StringComparison.OrdinalIgnoreCase);
            foreach (var g in Groups)
                g.IsVisible = g.Views.Any(v => v.IsVisible);
        }

        // ── Find & replace inputs ───────────────────────────────────────────

        /// <summary>Text to find.</summary>
        [ObservableProperty] private string find = string.Empty;

        /// <summary>Replacement text.</summary>
        [ObservableProperty] private string replaceWith = string.Empty;

        /// <summary>Added before the text of each note that changes.</summary>
        [ObservableProperty] private string prefix = string.Empty;

        /// <summary>Added after the text of each note that changes.</summary>
        [ObservableProperty] private string suffix = string.Empty;

        /// <summary>Match case.</summary>
        [ObservableProperty] private bool matchCase;

        /// <summary>Whole word.</summary>
        [ObservableProperty] private bool wholeWord;

        /// <summary>Find is a regular expression.</summary>
        [ObservableProperty] private bool useRegex;

        /// <summary>Trim spaces in notes that change.</summary>
        [ObservableProperty] private bool trimSpaces = true;

        /// <summary>Inline error under Find (invalid or too slow regex), or null.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasRegexError))]
        private string regexError;

        /// <summary>True when <see cref="RegexError"/> is shown.</summary>
        public bool HasRegexError => !string.IsNullOrEmpty(RegexError);

        partial void OnFindChanged(string value) => Recompute();
        partial void OnReplaceWithChanged(string value) => Recompute();
        partial void OnPrefixChanged(string value) => Recompute();
        partial void OnSuffixChanged(string value) => Recompute();
        partial void OnMatchCaseChanged(bool value) => Recompute();
        partial void OnWholeWordChanged(bool value) => Recompute();
        partial void OnUseRegexChanged(bool value) => Recompute();
        partial void OnTrimSpacesChanged(bool value) => Recompute();

        private ReplaceOptions CurrentOptions() => new()
        {
            Find = Find ?? string.Empty,
            Replacement = ReplaceWith ?? string.Empty,
            Prefix = Prefix ?? string.Empty,
            Suffix = Suffix ?? string.Empty,
            MatchCase = MatchCase,
            WholeWord = WholeWord,
            UseRegex = UseRegex,
            TrimSpaces = TrimSpaces
        };

        // ── Preview grid ────────────────────────────────────────────────────

        /// <summary>One row per matching text note.</summary>
        public ObservableCollection<PreviewRowViewModel> Rows { get; } = new();

        /// <summary>Hint above the grid.</summary>
        [ObservableProperty] private string previewHint = string.Empty;

        /// <summary>Text shown when the grid is empty.</summary>
        [ObservableProperty] private string emptyText = string.Empty;

        /// <summary>True when the grid has rows.</summary>
        [ObservableProperty] private bool hasRows;

        /// <summary>Header tick box: true = every changeable row ticked, false = none, null = some.</summary>
        public bool? AllRowsIncluded
        {
            get
            {
                var able = Rows.Where(r => r.IsEditable).ToList();
                if (able.Count == 0) return false;
                int on = able.Count(r => r.IsIncluded);
                return on == able.Count ? true : on == 0 ? false : null;
            }
            set
            {
                if (IsAfterPhase) return;
                bool target = value != false;
                _bulkRows = true;
                try
                {
                    foreach (var row in Rows.Where(r => r.IsEditable))
                        row.IsIncluded = target;
                }
                finally
                {
                    _bulkRows = false;
                }
                UpdateCounts();
            }
        }

        /// <summary>Header tick box enabled.</summary>
        public bool CanToggleAllRows => !IsAfterPhase && Rows.Any(r => r.IsEditable);

        private void OnRowIncludedChanged(PreviewRowViewModel row)
        {
            if (row.IsIncluded) _excluded.Remove(row.NoteId);
            else _excluded.Add(row.NoteId);
            if (!_bulkRows) UpdateCounts();
        }

        /// <summary>Shows the row's note in Revit (double-click).</summary>
        [RelayCommand]
        private void ShowNote(PreviewRowViewModel row)
        {
            if (row == null || IsRunning) return;
            bool accepted = _session.ShowNote(row.NoteId, error =>
            {
                if (error != null)
                    AddLog(LogLevel.Warning, error);
                else
                    AddLog(LogLevel.Info, row.View.IsSheetItself
                        ? $"Shown in Revit: \"{OneLine(row.Note.Text)}\" on the sheet, selected."
                        : $"Shown in Revit: \"{OneLine(row.Note.Text)}\" in {row.ViewName}, selected.");
            });
            if (!accepted) AddLog(LogLevel.Error, NotAccepted);
        }

        // ── Summary ─────────────────────────────────────────────────────────

        /// <summary>True after Apply until an input, option or view tick changes.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBeforePhase), nameof(PhaseText), nameof(CanToggleAllRows))]
        private bool isAfterPhase;

        /// <summary>True before Apply.</summary>
        public bool IsBeforePhase => !IsAfterPhase;

        /// <summary>"BEFORE APPLY" / "AFTER APPLY".</summary>
        public string PhaseText => IsAfterPhase ? "AFTER APPLY" : "BEFORE APPLY";

        /// <summary>BEFORE: "3/5".</summary>
        [ObservableProperty] private string viewsCheckedText = "0/0";

        /// <summary>BEFORE: text notes in the ticked views.</summary>
        [ObservableProperty] private int notesScanned;

        /// <summary>BEFORE: notes where Find occurs.</summary>
        [ObservableProperty] private int matchesFound;

        /// <summary>BEFORE: ticked rows that will change.</summary>
        [ObservableProperty] private int willChangeCount;

        /// <summary>BEFORE: rows the user unticked.</summary>
        [ObservableProperty] private int excludedCount;

        /// <summary>BEFORE: matches that cannot change (owned, group, read-only, no change).</summary>
        [ObservableProperty] private int willSkipCount;

        /// <summary>BEFORE: example old text.</summary>
        [ObservableProperty] private IReadOnlyList<TextSegment> exampleOld;

        /// <summary>BEFORE: example new text.</summary>
        [ObservableProperty] private IReadOnlyList<TextSegment> exampleNew;

        /// <summary>BEFORE: true when there is an example.</summary>
        [ObservableProperty] private bool hasExample;

        /// <summary>BEFORE: why there is no example.</summary>
        [ObservableProperty] private string exampleMessage = string.Empty;

        /// <summary>AFTER: notes changed.</summary>
        [ObservableProperty] private int changedCount;

        /// <summary>AFTER: notes skipped.</summary>
        [ObservableProperty] private int skippedCount;

        /// <summary>AFTER: notes Revit refused to change.</summary>
        [ObservableProperty] private int failedCount;

        /// <summary>AFTER: views with at least one changed note.</summary>
        [ObservableProperty] private int viewsAffected;

        /// <summary>AFTER: time inside Revit.</summary>
        [ObservableProperty] private long elapsedMs;

        /// <summary>AFTER: "N notes changed on … Undo with Ctrl+Z in Revit."</summary>
        [ObservableProperty] private string afterHeadline = string.Empty;

        /// <summary>AFTER: one line per skipped or failed note.</summary>
        public ObservableCollection<ResultLine> ResultLines { get; } = new();

        /// <summary>Footer text.</summary>
        [ObservableProperty] private string footerText = string.Empty;

        // ── Apply ───────────────────────────────────────────────────────────

        /// <summary>Applies the ticked rows in one undoable transaction.</summary>
        [RelayCommand(CanExecute = nameof(CanApply))]
        private void Apply()
        {
            var included = Rows.Where(r => r.CanApply && r.IsIncluded).ToList();
            var blocked = Rows.Where(r => !r.CanApply).ToList();
            var request = new ApplyRequest
            {
                SheetId = _content.Sheet.Id,
                Changes = included.Select(r => r.ToChange()).ToList()
            };
            string sheetLabel = _content.Sheet.Label;

            SetBusy(true, $"Applying {included.Count} change(s)…");
            AddLog(LogLevel.Info, $"Applying {included.Count} change(s) on {sheetLabel}…");
            bool accepted = _session.Apply(request, result => OnApplied(result, included, blocked, sheetLabel));
            if (!accepted)
            {
                SetBusy(false, string.Empty);
                AddLog(LogLevel.Error, NotAccepted);
            }
        }

        private bool CanApply()
            => !IsRunning && !IsAfterPhase && _content != null && !string.IsNullOrEmpty(Find)
               && !HasRegexError && WillChangeCount > 0;

        private void OnApplied(ApplyResult result, List<PreviewRowViewModel> included, List<PreviewRowViewModel> blocked, string sheetLabel)
        {
            SetBusy(false, string.Empty);
            if (!result.Success)
            {
                AddLog(LogLevel.Error, result.Error ?? "Nothing was changed.");
                if (result.Content != null)
                    SetContent(result.Content, resetChecks: false, recompute: true);
                return;
            }

            var skipped = result.Skipped.ToDictionary(i => i.NoteId);
            var failed = result.Failed.ToDictionary(i => i.NoteId);
            var changed = new HashSet<long>(result.ChangedIds);
            var reset = new HashSet<long>(result.FormattingReset.Select(i => i.NoteId));

            ResultLines.Clear();
            foreach (var row in included)
            {
                if (failed.TryGetValue(row.NoteId, out var f))
                {
                    row.SetResult(RowStatus.Failed, "Failed · " + f.Reason);
                    ResultLines.Add(new ResultLine($"{row.ViewName}: \"{OneLine(row.Note.Text)}\" failed ({f.Reason})", true));
                    AddLog(LogLevel.Error, $"Failed \"{OneLine(row.Note.Text)}\" in {row.ViewName}: {f.Reason}");
                }
                else if (skipped.TryGetValue(row.NoteId, out var s))
                {
                    row.SetResult(RowStatus.Skipped, "Skipped · " + s.Reason);
                    ResultLines.Add(new ResultLine($"{row.ViewName}: \"{OneLine(row.Note.Text)}\" skipped ({s.Reason})", false));
                    AddLog(LogLevel.Warning, s.Reason == "Changed since preview"
                        ? $"Skipped \"{OneLine(row.Note.Text)}\" in {row.ViewName}: it was changed in Revit after the preview. Press Refresh and check it again."
                        : $"Skipped \"{OneLine(row.Note.Text)}\" in {row.ViewName}: {s.Reason}.");
                }
                else if (changed.Contains(row.NoteId))
                {
                    row.SetResult(RowStatus.Changed, reset.Contains(row.NoteId) ? "Changed · formatting reset" : "Changed");
                }
            }
            foreach (var row in blocked)
            {
                row.SetResult(RowStatus.Skipped, "Skipped · " + row.BlockReason);
                ResultLines.Add(new ResultLine($"{row.ViewName}: \"{OneLine(row.Note.Text)}\" skipped ({row.BlockReason})", false));
                AddLog(LogLevel.Warning, $"Skipped \"{OneLine(row.Note.Text)}\" in {row.ViewName}: {row.BlockReason}.");
            }
            foreach (var issue in result.FormattingReset)
                AddLog(LogLevel.Warning, $"\"{OneLine(issue.Text)}\" in {issue.ViewName}: formatting could not be kept, so the note was set as plain text.");

            // The grid keeps only the rows that were applied or skipped.
            foreach (var row in Rows.Where(r => r.CanApply && !r.IsIncluded).ToList())
                Rows.Remove(row);

            var changedRows = included.Where(r => changed.Contains(r.NoteId)).ToList();
            foreach (var view in changedRows.Select(r => r.View).Where(v => v.AlsoOnSheets.Count > 0).Distinct())
                AddLog(LogLevel.Info, $"{view.Name} is also on {string.Join(", ", view.AlsoOnSheets)}: the change shows there too.");

            ChangedCount = changed.Count;
            SkippedCount = result.Skipped.Count + blocked.Count;
            FailedCount = result.Failed.Count;
            ViewsAffected = changedRows.Select(r => r.View.Key).Distinct().Count();
            ElapsedMs = result.ElapsedMs;
            AfterHeadline = ChangedCount > 0
                ? $"{ChangedCount} note{(ChangedCount == 1 ? "" : "s")} changed on {sheetLabel} in one transaction. Undo with Ctrl+Z in Revit."
                : $"Nothing was changed on {sheetLabel}.";
            AddLog(ChangedCount > 0 ? LogLevel.Success : LogLevel.Warning,
                $"{ChangedCount} note(s) changed in {ViewsAffected} view(s), {SkippedCount} skipped, {FailedCount} failed ({ElapsedMs} ms). One undo step: \"{TextReplaceApplier.TransactionName}\".");

            IsAfterPhase = true;
            FooterText = "Done. Change the inputs or press Refresh to preview again.";
            PreviewHint = "Result of the last Apply · double-click a row to show it in Revit";
            OnPropertyChanged(nameof(AllRowsIncluded));
            ApplyCommand.NotifyCanExecuteChanged();

            if (result.Content != null)
                SetContent(result.Content, resetChecks: false, recompute: false);
        }

        // ── Rebuilding ──────────────────────────────────────────────────────

        private void SetContent(SheetContent content, bool resetChecks, bool recompute)
        {
            var previousChecks = _viewRows.ToDictionary(v => v.Key, v => v.IsChecked);
            var previousNotes = new HashSet<long>(AllNotes(_content).Select(n => n.Id));

            _suspend = true;
            try
            {
                _content = content;
                if (resetChecks) _excluded.Clear();

                // Notes of views shown on other sheets (legends) start unticked.
                foreach (var view in content.Views.Where(v => v.AlsoOnSheets.Count > 0))
                    foreach (var note in view.Notes.Where(n => resetChecks || !previousNotes.Contains(n.Id)))
                        _excluded.Add(note.Id);

                _viewRows.Clear();
                Groups.Clear();
                foreach (var view in content.Views)
                {
                    bool check = resetChecks || !previousChecks.TryGetValue(view.Key, out bool was) || was;
                    _viewRows.Add(new ViewRowViewModel(view, check, OnViewCheckedChanged));
                }

                foreach (var group in _viewRows.GroupBy(v => v.TypeLabel)
                             .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Groups.Add(new ViewTypeGroupViewModel(group.Key, group, RunBatched));
                }
                ApplyViewFilter();
            }
            finally
            {
                _suspend = false;
            }

            OnPropertyChanged(nameof(CurrentSheet));
            OnPropertyChanged(nameof(CurrentSheetLabel));
            OnPropertyChanged(nameof(HasSheet));
            OnPropertyChanged(nameof(ViewsButtonText));
            RefreshCommand.NotifyCanExecuteChanged();
            if (recompute) Recompute();
        }

        private void Recompute()
        {
            if (_suspend) return;
            LeaveAfterPhase();

            Rows.Clear();
            var options = CurrentOptions();
            bool findEmpty = string.IsNullOrEmpty(options.Find);
            string error = null;
            Regex regex = null;
            if (!findEmpty)
                ReplaceEngine.TryBuildRegex(options, out regex, out error);

            int scanned = 0;
            var rows = new List<PreviewRowViewModel>();
            if (_content != null)
            {
                var ticked = new HashSet<long>(_viewRows.Where(v => v.IsChecked).Select(v => v.Key));
                foreach (var view in _content.Views.Where(v => ticked.Contains(v.Key)))
                {
                    foreach (var note in view.Notes)
                    {
                        scanned++;
                        if (regex == null) continue;

                        ReplaceOutcome outcome;
                        try
                        {
                            outcome = ReplaceEngine.Run(note.Text, options, regex);
                        }
                        catch (RegexMatchTimeoutException)
                        {
                            error = "The regular expression takes too long on some notes. Make it simpler.";
                            regex = null;
                            rows.Clear();
                            continue;
                        }

                        if (!outcome.Matched) continue;
                        string block = note.BlockReason ?? (outcome.Changed ? null : "No change");
                        rows.Add(new PreviewRowViewModel(view, note, outcome, block, !_excluded.Contains(note.Id), OnRowIncludedChanged));
                    }
                }
            }

            foreach (var row in rows) Rows.Add(row);
            RegexError = findEmpty ? null : error;
            NotesScanned = scanned;
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            int able = Rows.Count(r => r.CanApply);
            int will = Rows.Count(r => r.CanApply && r.IsIncluded);
            int tickedViews = _viewRows.Count(v => v.IsChecked);

            ViewsCheckedText = $"{tickedViews}/{_viewRows.Count}";
            MatchesFound = Rows.Count;
            WillChangeCount = will;
            ExcludedCount = able - will;
            WillSkipCount = Rows.Count - able;
            HasRows = Rows.Count > 0;

            string message =
                _content == null ? "Pick a sheet to see a preview." :
                string.IsNullOrEmpty(Find) ? "Type something in Find to see a preview." :
                HasRegexError ? "Fix the regular expression to see a preview." :
                tickedViews == 0 ? "Tick at least one view (Views… button)." :
                Rows.Count == 0 ? "No text notes match in the ticked views." :
                will == 0 ? "Nothing will change: every match is unticked or skipped." :
                string.Empty;

            var example = Rows.FirstOrDefault(r => r.CanApply && r.IsIncluded);
            HasExample = example != null;
            ExampleOld = example?.OldSegments;
            ExampleNew = example?.NewSegments;
            ExampleMessage = message;
            EmptyText = Rows.Count == 0 ? message : string.Empty;

            PreviewHint = Rows.Count > 0
                ? $"{Rows.Count} matching note(s) · only ticked rows are applied · double-click a row to show it in Revit"
                : string.Empty;
            FooterText = $"{will} change{(will == 1 ? "" : "s")} will be made in one undoable transaction";

            OnPropertyChanged(nameof(AllRowsIncluded));
            OnPropertyChanged(nameof(CanToggleAllRows));
            ApplyCommand.NotifyCanExecuteChanged();
        }

        private void LeaveAfterPhase()
        {
            if (!IsAfterPhase) return;
            IsAfterPhase = false;
            ResultLines.Clear();
        }

        private void LogSheetLoaded(SheetContent content)
        {
            int notes = AllNotes(content).Count();
            int viewports = content.Views.Count(v => !v.IsSheetItself);
            int types = content.Views.Where(v => !v.IsSheetItself).Select(v => v.TypeLabel).Distinct().Count();
            string onSheet = content.Views.Any(v => v.IsSheetItself) ? " plus notes placed on the sheet itself" : string.Empty;
            AddLog(LogLevel.Info, $"Sheet {content.Sheet.Label}: {viewports} view(s) in {types} type(s){onSheet}, {notes} text note(s).");

            var empty = content.Views.Where(v => v.Notes.Count == 0).Select(v => v.Name).ToList();
            if (empty.Count > 0)
                AddLog(LogLevel.Info, $"No text notes in: {string.Join(", ", empty.Take(5))}{(empty.Count > 5 ? $" and {empty.Count - 5} more" : "")}.");

            foreach (var view in content.Views.Where(v => v.Owner != null))
                AddLog(LogLevel.Warning, $"{view.Name} is owned by {view.Owner}: its notes will be skipped.");
            foreach (var view in content.Views.Where(v => v.AlsoOnSheets.Count > 0))
                AddLog(LogLevel.Warning, $"{view.Name} is also on {string.Join(", ", view.AlsoOnSheets)}: its notes start unticked.");

            int reload = AllNotes(content).Count(n => n.BlockReason == "Reload latest first");
            if (reload > 0)
                AddLog(LogLevel.Warning, $"{reload} note(s) changed in central: use Reload Latest before they can be edited.");
        }

        private void SetBusy(bool busy, string status)
        {
            IsRunning = busy;
            SummaryText = status;
            ApplyCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
        }

        private static IEnumerable<NoteInfo> AllNotes(SheetContent content)
            => content?.Views.SelectMany(v => v.Notes) ?? Enumerable.Empty<NoteInfo>();

        private static int Rank(string type)
        {
            int i = Array.IndexOf(TypeOrder, type);
            return i < 0 ? TypeOrder.Length : i;
        }

        /// <summary>Note text on one line for messages ("A ↵ B").</summary>
        public static string OneLine(string text)
            => (text ?? string.Empty).Replace("\r\n", "\r").Replace('\n', '\r').TrimEnd('\r').Replace("\r", " ↵ ");

        /// <summary>Releases the Revit session.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}
