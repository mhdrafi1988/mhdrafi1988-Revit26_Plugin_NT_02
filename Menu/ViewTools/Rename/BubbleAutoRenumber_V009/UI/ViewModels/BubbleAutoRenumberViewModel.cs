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
    /// <summary>One entry in the label-style drop-down.</summary>
    public sealed class NumberStyleChoice
    {
        /// <summary>Creates a choice.</summary>
        public NumberStyleChoice(NumberStyle style, string label) { Style = style; Label = label; }

        /// <summary>The style this entry selects.</summary>
        public NumberStyle Style { get; }

        /// <summary>Example text shown to the user.</summary>
        public string Label { get; }
    }

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

        /// <summary>Label styles offered in the window, with an example of each.</summary>
        public System.Collections.Generic.IReadOnlyList<NumberStyleChoice> StyleChoices { get; } = new[]
        {
            new NumberStyleChoice(NumberStyle.Numeric, "1, 2, 3"),
            new NumberStyleChoice(NumberStyle.ZeroPadded, "01, 02, 03"),
            new NumberStyleChoice(NumberStyle.UpperLetters, "A, B, C"),
            new NumberStyleChoice(NumberStyle.LowerLetters, "a, b, c")
        };

        [ObservableProperty] private NumberStyle _numberStyle = NumberStyle.Numeric;

        partial void OnStartingNumberChanged(string value) => RefreshPreview();

        partial void OnNumberStyleChanged(NumberStyle oldValue, NumberStyle value)
        {
            // Re-express the start in the new style ("3" → "C"); keep the text if it was invalid.
            if (NumberFormatter.TryParse(StartingNumber, oldValue, out int index))
                StartingNumber = NumberFormatter.Format(index, value);
            else
                StartingNumber = NumberFormatter.Format(1, value);

            RefreshPreview();
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

            if (!NumberFormatter.TryParse(StartingNumber, NumberStyle, out int start))
            {
                ValidationMessage = NumberStyle is NumberStyle.UpperLetters or NumberStyle.LowerLetters
                    ? "Starting value must be letters only (A, B … Z, AA …)."
                    : "Starting number must be a whole number above zero.";
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
                Style = NumberStyle,
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
                + $"starting at {StartingNumber.Trim()}.");

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
