using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Services;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.UI.ViewModels
{
    /// <summary>
    /// CHANGED in V009:
    /// - Derives from Shared/ViewModels/ToolViewModelBase, so LogEntries, Copy All /
    ///   Copy Selected / Clear and IsRunning / Progress / SummaryText all come from the
    ///   shared base. V008 kept a flat LogText string and declared the three status
    ///   properties without ever setting them, which left the shell's footer strip
    ///   permanently blank.
    /// - The grid is a live preview of the actual plan, not just current numbers: the
    ///   same RenumberPlanner that writes the model fills the New # column.
    /// - Type filter for sections / drafting / detail / elevation views, plus the
    ///   any-numbered-view override (NEW).
    /// - Dropped V008's unused _uidoc field and its unused HasResults property.
    /// </summary>
    public partial class BubbleAutoRenumberViewModel : ToolViewModelBase
    {
        private readonly Document _doc;
        private readonly BubbleAutoRenumberHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher;

        private RenumberSummary _lastSummary;

        /// <summary>Shown in the shell header, from the one constant that defines it.</summary>
        public string Version => BubbleAutoRenumberTool.ShellVersion;

        // ── Sheet ─────────────────────────────────────────────────────────────────
        public ObservableCollection<ViewSheet> Sheets { get; } = new();

        [ObservableProperty] private ViewSheet _selectedSheet;

        partial void OnSelectedSheetChanged(ViewSheet value) => RefreshPreview();

        public string SelectedSheetName => SelectedSheet?.Name ?? string.Empty;

        // ── Inputs ────────────────────────────────────────────────────────────────
        [ObservableProperty] private string _startingNumber = "1";
        [ObservableProperty] private string _verticalThreshold = "20";

        /// <summary>
        /// Numbering style (NEW in 9.3.0): false writes 1, 2, 3 (the default, as before),
        /// true writes A, B, C … Z, AA, AB.
        /// </summary>
        [ObservableProperty] private bool _useLetters;

        /// <summary>Hint next to the start box, for the style in use.</summary>
        public string StartHint => UseLetters
            ? "letters A to Z — after Z comes AA, AB …"
            : "whole number above zero";

        partial void OnStartingNumberChanged(string value) => RefreshPreview();

        /// <summary>
        /// Carries the start value over to the new style (3 ↔ C), so switching style
        /// keeps the same place in the sequence instead of leaving an invalid entry.
        /// </summary>
        partial void OnUseLettersChanged(bool value)
        {
            OnPropertyChanged(nameof(StartHint));

            NumberingStyle previous = value ? NumberingStyle.Numbers : NumberingStyle.Letters;
            NumberingStyle current = value ? NumberingStyle.Letters : NumberingStyle.Numbers;

            string converted = SequenceLabel.TryParseStart(StartingNumber, previous, out int index)
                ? SequenceLabel.Format(index, current)
                : StartingNumber;

            // A start too large for three letters stays as typed; validation then says why.
            if (value && !SequenceLabel.TryParseStart(converted, current, out _))
                converted = StartingNumber;

            if (converted != StartingNumber) StartingNumber = converted;   // refreshes the preview
            else RefreshPreview();
        }
        partial void OnVerticalThresholdChanged(string value) => RefreshPreview();

        // ── Type filter (NEW in V009) ─────────────────────────────────────────────
        [ObservableProperty] private bool _includeSections = true;
        [ObservableProperty] private bool _includeDrafting = true;
        [ObservableProperty] private bool _includeDetail = true;
        [ObservableProperty] private bool _includeElevations = true;
        [ObservableProperty] private bool _anyNumberedView;

        partial void OnIncludeSectionsChanged(bool value) => RefreshPreview();
        partial void OnIncludeDraftingChanged(bool value) => RefreshPreview();
        partial void OnIncludeDetailChanged(bool value) => RefreshPreview();
        partial void OnIncludeElevationsChanged(bool value) => RefreshPreview();
        partial void OnAnyNumberedViewChanged(bool value) => RefreshPreview();

        // ── Preview ───────────────────────────────────────────────────────────────
        public ObservableCollection<RenumberPreviewRow> PreviewRows { get; } = new();

        /// <summary>Viewports on the sheet, whatever the filter.</summary>
        [ObservableProperty] private int _onSheetCount;

        /// <summary>Rows the filter put in scope, including read-only ones.</summary>
        [ObservableProperty] private int _inScopeCount;

        /// <summary>Rows that will actually receive a number.</summary>
        [ObservableProperty] private int _targetCount;

        /// <summary>Why Renumber is disabled, when it is.</summary>
        [ObservableProperty] private string _validationMessage = string.Empty;

        // ── Last run ──────────────────────────────────────────────────────────────
        [ObservableProperty] private int _renumberedCount;
        [ObservableProperty] private int _skippedCount;
        [ObservableProperty] private int _failedCount;
        [ObservableProperty] private bool _hasReport;

        public BubbleAutoRenumberViewModel(UIDocument uidoc)
        {
            _doc = uidoc.Document;
            _dispatcher = Dispatcher.CurrentDispatcher;

            BubbleAutoRenumberEventManager.Init();
            _handler = BubbleAutoRenumberEventManager.Handler;
            _externalEvent = BubbleAutoRenumberEventManager.Event;

            _handler.Completed = OnRunCompleted;
            _handler.ProgressChanged = OnProgressChanged;

            LoadSheets();

            // Selecting the first sheet raises OnSelectedSheetChanged, which builds the
            // preview. A model with no sheets selects nothing, so prime it by hand —
            // otherwise the window opens with no rows and no reason given.
            if (SelectedSheet == null) RefreshPreview();

            AddLog(LogLevel.Info,
                $"{BubbleAutoRenumberTool.Name} {BubbleAutoRenumberTool.ShellVersion} ready — "
                + $"{Sheets.Count} sheet(s) in the model.");
        }

        // ── Sheet loading ─────────────────────────────────────────────────────────
        private void LoadSheets()
        {
            var all = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            // Active sheet first, so the common case needs no picking.
            if (_doc.ActiveView is ViewSheet active)
            {
                ViewSheet found = all.FirstOrDefault(s => s.Id == active.Id);

                if (found != null)
                {
                    all.Remove(found);
                    all.Insert(0, found);
                }
            }

            foreach (ViewSheet sheet in all) Sheets.Add(sheet);

            SelectedSheet = Sheets.FirstOrDefault();
        }

        // ── Preview ───────────────────────────────────────────────────────────────
        private void RefreshPreview()
        {
            PreviewRows.Clear();
            OnPropertyChanged(nameof(SelectedSheetName));

            OnSheetCount = 0;
            InScopeCount = 0;
            TargetCount = 0;

            if (SelectedSheet == null)
            {
                ValidationMessage = "No sheet selected.";
                RenumberCommand.NotifyCanExecuteChanged();
                return;
            }

            // Read-only Revit access from the modeless window's UI thread, exactly as
            // V008's RefreshGrid did. Deliberately left as is — writes all go through
            // the external event; see CLAUDE.md on not "fixing" confirmed patterns.
            var rows = ViewportReader.Read(_doc, SelectedSheet);
            OnSheetCount = rows.Count;

            if (!TryBuildOptions(out RenumberOptions options))
            {
                RenumberCommand.NotifyCanExecuteChanged();
                return;
            }

            RenumberPlan plan = RenumberPlanner.Plan(rows, options);

            foreach (RenumberPreviewRow row in plan.Preview) PreviewRows.Add(row);

            InScopeCount = plan.Preview.Count;
            TargetCount = plan.Assignments.Count;

            ValidationMessage = TargetCount == 0
                ? "Nothing to renumber on this sheet with the current view-type filter."
                : string.Empty;

            RenumberCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Parses and validates the inputs. Sets ValidationMessage and returns false
        /// when something is wrong, so the reason is on screen rather than implied by a
        /// greyed-out button.
        /// </summary>
        private bool TryBuildOptions(out RenumberOptions options)
        {
            options = null;

            NumberingStyle style = UseLetters ? NumberingStyle.Letters : NumberingStyle.Numbers;

            if (!SequenceLabel.TryParseStart(StartingNumber, style, out int start))
            {
                ValidationMessage = UseLetters
                    ? "Start at must be one to three letters, A to Z (for example A, C or AA)."
                    : "Start at must be a whole number above zero.";
                return false;
            }

            if (!double.TryParse(VerticalThreshold, out double mm) || mm <= 0)
            {
                ValidationMessage = "Vertical threshold must be a number above zero (mm).";
                return false;
            }

            var built = new RenumberOptions
            {
                StartNumber = start,
                Style = style,
                ThresholdFt = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters),
                IncludeSections = IncludeSections,
                IncludeDrafting = IncludeDrafting,
                IncludeDetail = IncludeDetail,
                IncludeElevations = IncludeElevations,
                AnyNumberedView = AnyNumberedView
            };

            if (!built.HasAnyTypeSelected)
            {
                ValidationMessage = "Select at least one view type to renumber.";
                return false;
            }

            ValidationMessage = string.Empty;
            options = built;
            return true;
        }

        // ── Run ───────────────────────────────────────────────────────────────────
        [RelayCommand(CanExecute = nameof(CanRenumber))]
        private void Renumber()
        {
            if (!TryBuildOptions(out RenumberOptions options)) return;
            if (SelectedSheet == null) return;

            IsRunning = true;
            Progress = 0;
            SummaryText = $"Renumbering {SelectedSheet.SheetNumber}…";
            RenumberCommand.NotifyCanExecuteChanged();

            AddLog(LogLevel.Info,
                $"Run started on {SelectedSheet.SheetNumber} — {TargetCount} view(s) in scope, "
                + $"{(UseLetters ? "letters" : "numbers")} starting at {options.StartLabel}.");

            _handler.Request = new RenumberRequest
            {
                Sheet = SelectedSheet,
                Options = options
            };

            _externalEvent.Raise();
        }

        private bool CanRenumber() => !IsRunning && SelectedSheet != null && TargetCount > 0;

        // ── Callbacks from the Revit thread ───────────────────────────────────────
        private void OnProgressChanged(int done, int total)
        {
            if (total <= 0) return;

            // Async: the Revit thread should not wait on the UI thread for a progress tick.
            _dispatcher.InvokeAsync(() => Progress = done * 100.0 / total);
        }

        private void OnRunCompleted(RenumberSummary summary)
        {
            _dispatcher.Invoke(() =>
            {
                _lastSummary = summary;
                HasReport = true;

                RenumberedCount = summary.Renumbered;
                SkippedCount = summary.Skipped;
                FailedCount = summary.Failed;

                foreach (RenumberResult result in summary.Results)
                    AddLog(LevelFor(result.Outcome), Describe(result));

                if (!string.IsNullOrEmpty(summary.ErrorMessage))
                    AddLog(LogLevel.Error, $"Run failed: {summary.ErrorMessage}");

                SummaryText = summary.Headline;
                AddLog(summary.Failed > 0 ? LogLevel.Warning : LogLevel.Success, summary.Headline);

                Progress = 100;
                IsRunning = false;
                RenumberCommand.NotifyCanExecuteChanged();

                // Re-read so the grid shows what the model now holds.
                RefreshPreview();
            });
        }

        private static LogLevel LevelFor(RenumberOutcome outcome) => outcome switch
        {
            RenumberOutcome.Renumbered => LogLevel.Success,
            RenumberOutcome.Unchanged => LogLevel.Info,
            RenumberOutcome.Skipped => LogLevel.Warning,
            _ => LogLevel.Error
        };

        private static string Describe(RenumberResult result)
        {
            string was = string.IsNullOrEmpty(result.PreviousNumber) ? "(none)" : result.PreviousNumber;

            return result.Outcome switch
            {
                RenumberOutcome.Renumbered =>
                    $"{result.KindLabel} · {result.ViewName}: {was} → {result.NewNumber}",
                RenumberOutcome.Unchanged =>
                    $"{result.KindLabel} · {result.ViewName}: already {result.NewNumber}",
                RenumberOutcome.Skipped =>
                    $"{result.KindLabel} · {result.ViewName}: skipped — {result.Message}",
                _ =>
                    $"{result.KindLabel} · {result.ViewName}: failed — {result.Message}"
            };
        }

        // ── Report ────────────────────────────────────────────────────────────────
        [RelayCommand]
        private void CopyReport()
        {
            if (_lastSummary == null) return;

            try { Clipboard.SetText(_lastSummary.ToReportText()); }
            catch { /* clipboard busy — same best-effort handling as ToolViewModelBase */ }
        }

        /// <summary>Clears the run metrics alongside the log.</summary>
        protected override void OnClearLog()
        {
            base.OnClearLog();

            RenumberedCount = 0;
            SkippedCount = 0;
            FailedCount = 0;
            HasReport = false;
            _lastSummary = null;
            SummaryText = string.Empty;
            Progress = 0;
        }
    }
}
