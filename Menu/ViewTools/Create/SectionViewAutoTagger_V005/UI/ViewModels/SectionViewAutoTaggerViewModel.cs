using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V005
{
    public partial class SectionViewAutoTaggerViewModel : ObservableObject
    {
        private readonly UIApplication _uiApp;
        private readonly Dispatcher _dispatcher;
        private readonly SettingsService _settingsService = new();

        private readonly ExternalEvent _scanEvent;
        private readonly ScanEventHandler _scanHandler;

        /// <summary>
        /// Set when a scan is requested while one is already pending; re-run
        /// once the in-flight scan completes, so a rapid double-toggle isn't
        /// silently dropped (see ScanEventHandler.IsPending).
        /// </summary>
        private Action _queuedScanRequest;

        private readonly ExternalEvent _placeEvent;
        private readonly PlaceTagsEventHandler _placeHandler;

        // ── Sheet / View selection ──────────────────────────────────────
        [ObservableProperty]
        private ObservableCollection<SheetOption> sheets = new();

        [ObservableProperty]
        private SheetOption selectedSheet;

        [ObservableProperty]
        private ObservableCollection<SectionViewOption> sectionViewsOnSheet = new();

        /// <summary>
        /// V003: display string for the multi-select popover's closed-state
        /// chip row / trigger text. Recomputed whenever a view's IsSelected
        /// changes (see OnSectionViewOptionChanged) or the view list itself
        /// is replaced.
        /// </summary>
        public string SelectedViewsSummary
        {
            get
            {
                var chosen = SectionViewsOnSheet.Where(v => v.IsSelected).ToList();
                if (chosen.Count == 0) return "No views selected";
                return string.Join(", ", chosen.Select(v => v.ViewName));
            }
        }

        // ── Category checklist ──────────────────────────────────────────
        [ObservableProperty]
        private ObservableCollection<CategoryTagRow> categories = new();

        // ── Worklist ─────────────────────────────────────────────────────
        [ObservableProperty]
        private ObservableCollection<WorklistEntry> worklist = new();

        /// <summary>
        /// Derived bool for empty-state visibility binding. Updated manually
        /// via UpdateWorklistDerivedState() on every Add/Remove/Clear, since
        /// ObservableCollection doesn't raise INotifyPropertyChanged for
        /// Count changes on its own (only CollectionChanged).
        /// </summary>
        public bool HasWorklistItems => Worklist.Count > 0;

        private void UpdateWorklistDerivedState()
        {
            OnPropertyChanged(nameof(HasWorklistItems));
            AddToWorklistCommand.NotifyCanExecuteChanged();
            RunCommand.NotifyCanExecuteChanged();
        }

        // ── Global settings ──────────────────────────────────────────────
        [ObservableProperty]
        private AlignmentSide alignmentSide;

        /// <summary>
        /// RadioButton-friendly bool views over AlignmentSide. Replaces an
        /// earlier attempt to bind AlignmentSide (enum) directly through
        /// InverseBoolConverter, which only handles bool — that binding was
        /// broken and has been replaced with this pair instead of a new
        /// enum converter, since two explicit properties are simpler and
        /// less error-prone here than a converter + parameter string match.
        /// </summary>
        public bool IsLeftSelected
        {
            get => AlignmentSide == AlignmentSide.Left;
            set
            {
                if (value && AlignmentSide != AlignmentSide.Left)
                {
                    AlignmentSide = AlignmentSide.Left;
                    OnPropertyChanged(nameof(IsLeftSelected));
                    OnPropertyChanged(nameof(IsRightSelected));
                }
            }
        }

        public bool IsRightSelected
        {
            get => AlignmentSide == AlignmentSide.Right;
            set
            {
                if (value && AlignmentSide != AlignmentSide.Right)
                {
                    AlignmentSide = AlignmentSide.Right;
                    OnPropertyChanged(nameof(IsLeftSelected));
                    OnPropertyChanged(nameof(IsRightSelected));
                }
            }
        }

        [ObservableProperty]
        private double offsetMm;

        [ObservableProperty]
        private double spacingMm;

        /// <summary>
        /// V003: global Leader End setting. Same RadioButton-friendly
        /// bool-pair pattern as IsLeftSelected/IsRightSelected above — kept
        /// consistent rather than introducing an enum converter for just
        /// this one field.
        /// </summary>
        [ObservableProperty]
        private LeaderEndCondition leaderEndCondition;

        public bool IsFreeEndSelected
        {
            get => LeaderEndCondition == LeaderEndCondition.Free;
            set
            {
                if (value && LeaderEndCondition != LeaderEndCondition.Free)
                {
                    LeaderEndCondition = LeaderEndCondition.Free;
                    OnPropertyChanged(nameof(IsFreeEndSelected));
                    OnPropertyChanged(nameof(IsAttachedEndSelected));
                }
            }
        }

        public bool IsAttachedEndSelected
        {
            get => LeaderEndCondition == LeaderEndCondition.Attached;
            set
            {
                if (value && LeaderEndCondition != LeaderEndCondition.Attached)
                {
                    LeaderEndCondition = LeaderEndCondition.Attached;
                    OnPropertyChanged(nameof(IsFreeEndSelected));
                    OnPropertyChanged(nameof(IsAttachedEndSelected));
                }
            }
        }

        // ── Run state ────────────────────────────────────────────────────
        [ObservableProperty]
        private bool isRunning;

        [ObservableProperty]
        private string summaryLine = string.Empty;

        [ObservableProperty]
        private ObservableCollection<LogEntry> logEntries = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSavedLogPath))]
        private string savedLogPath = string.Empty;

        /// <summary>Bound to the log ListBox's SelectedItems via code-behind (multi-select) for Copy Selected.</summary>
        public ObservableCollection<LogEntry> SelectedLogEntries { get; } = new();

        /// <summary>
        /// Derived bool for visibility binding. Replaces an earlier attempt
        /// to run SavedLogPath (string) through BoolToVisibilityConverter
        /// (which only accepts bool) — that binding was broken.
        /// </summary>
        public bool HasSavedLogPath => !string.IsNullOrEmpty(SavedLogPath);

        private readonly LogExportHelper _logExportHelper = new();

        public SectionViewAutoTaggerViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _dispatcher = Dispatcher.CurrentDispatcher;

            var settings = _settingsService.Load();
            alignmentSide = settings.AlignmentSide;
            offsetMm = settings.OffsetMm;
            spacingMm = settings.SpacingMm;
            leaderEndCondition = settings.LeaderEndCondition;

            _scanHandler = new ScanEventHandler();
            _scanHandler.Completed += OnScanCompleted;
            _scanEvent = ExternalEvent.Create(_scanHandler);

            _placeHandler = new PlaceTagsEventHandler();
            _placeHandler.LogSink = (level, msg) => AppendLog(level, msg);
            _placeHandler.Completed += OnPlaceCompleted;
            _placeEvent = ExternalEvent.Create(_placeHandler);

            // Kick off initial sheet load.
            _scanHandler.Mode = ScanEventHandler.ScanMode.LoadSheets;
            _scanEvent.Raise();
        }

        /// <summary>
        /// Guards against overwriting ScanEventHandler's fields while a
        /// scan is already in flight. If busy, the request is queued and
        /// re-run automatically once the current scan's Completed fires.
        /// </summary>
        private void RequestScan(Action setupAndRaise)
        {
            if (_scanHandler.IsPending)
            {
                _queuedScanRequest = setupAndRaise;
                return;
            }

            setupAndRaise();
        }

        partial void OnSelectedSheetChanged(SheetOption value)
        {
            SectionViewsOnSheet.Clear();
            Categories.Clear();
            OnPropertyChanged(nameof(SelectedViewsSummary));
            AddToWorklistCommand.NotifyCanExecuteChanged();

            if (value == null) return;

            RequestScan(() =>
            {
                _scanHandler.Mode = ScanEventHandler.ScanMode.LoadSectionViewsForSheet;
                _scanHandler.RequestedSheetId = value.SheetId;
                _scanEvent.Raise();
            });
        }

        [RelayCommand]
        private void RefreshCategories()
        {
            var checkedViewIds = SectionViewsOnSheet
                .Where(v => v.IsSelected)
                .Select(v => v.ViewId)
                .ToList();

            if (checkedViewIds.Count == 0)
            {
                Categories.Clear();
                return;
            }

            RequestScan(() =>
            {
                _scanHandler.Mode = ScanEventHandler.ScanMode.ScanCategoriesForViews;
                _scanHandler.RequestedViewIds = checkedViewIds;
                _scanEvent.Raise();
            });
        }

        [RelayCommand]
        private void SelectAllCategories()
        {
            foreach (var c in Categories.Where(c => c.IsTaggable))
                c.IsSelected = true;
        }

        [RelayCommand]
        private void ClearCategories()
        {
            foreach (var c in Categories)
                c.IsSelected = false;
        }

        [RelayCommand]
        private void SelectAllSectionViews()
        {
            foreach (var v in SectionViewsOnSheet)
                v.IsSelected = true;
        }

        [RelayCommand]
        private void ClearSectionViews()
        {
            foreach (var v in SectionViewsOnSheet)
                v.IsSelected = false;
        }

        [RelayCommand(CanExecute = nameof(CanAddToWorklist))]
        private void AddToWorklist()
        {
            if (SelectedSheet == null) return;

            var checkedViews = SectionViewsOnSheet.Where(v => v.IsSelected).ToList();
            var checkedCategories = Categories.Where(c => c.IsSelected && c.IsTaggable).ToList();

            if (checkedViews.Count == 0 || checkedCategories.Count == 0)
            {
                AppendLog(LogLevel.Warning, "Add to Worklist skipped — no views or no taggable categories checked.");
                return;
            }

            // V003: lock in each category's currently-selected tag type
            // (CategoryTagRow.SelectedTagType, a TagTypeOption wrapper)
            // into a CategoryTagSelection. Confirmed: resolved once here,
            // NOT re-resolved at Run time.
            var categorySelections = checkedCategories
                .Where(c => c.SelectedTagType != null)
                .Select(c => new CategoryTagSelection(
                    c.Category,
                    c.CategoryName,
                    c.SelectedTagType.Symbol.Id,
                    c.SelectedTagType.DisplayName))
                .ToList();

            if (categorySelections.Count == 0)
            {
                AppendLog(LogLevel.Warning, "Add to Worklist skipped — no resolvable tag type for any checked category.");
                return;
            }

            var entry = new WorklistEntry(
                SelectedSheet.SheetId,
                SelectedSheet.SheetNumber,
                SelectedSheet.SheetName,
                checkedViews,
                categorySelections,
                string.Join(", ", categorySelections.Select(c => $"{c.CategoryName} ({c.TagTypeName})")));

            Worklist.Add(entry);
            UpdateWorklistDerivedState();
            AppendLog(LogLevel.Info, $"Added to worklist: {entry.SheetDisplay} — {entry.CategoriesDisplay}");

            // V003: sheet selection is intentionally NOT cleared here —
            // confirmed behavior is to stay on the same sheet after Add to
            // Worklist so multiple category/view combinations from the same
            // sheet can be queued without re-picking it each time.
        }

        private bool CanAddToWorklist() => SelectedSheet != null;

        [RelayCommand]
        private void RemoveWorklistEntry(WorklistEntry entry)
        {
            if (entry == null) return;
            Worklist.Remove(entry);
            UpdateWorklistDerivedState();
        }

        [RelayCommand]
        private void ClearWorklist()
        {
            Worklist.Clear();
            UpdateWorklistDerivedState();
        }

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            if (Worklist.Count == 0) return;

            IsRunning = true;
            LogEntries.Clear();
            SummaryLine = string.Empty;

            var settings = new TagPlacementSettings
            {
                AlignmentSide = AlignmentSide,
                OffsetMm = OffsetMm,
                SpacingMm = SpacingMm,
                LeaderEndCondition = LeaderEndCondition
            };

            _placeHandler.Worklist = Worklist.ToList();
            _placeHandler.Settings = settings;
            _placeEvent.Raise();

            // V003: sheet selection is intentionally NOT cleared here —
            // same confirmed behavior as AddToWorklist above.
        }

        private bool CanRun() => !IsRunning && Worklist.Count > 0;

        partial void OnIsRunningChanged(bool value)
        {
            RunCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Named handler (not a lambda) so it can be unsubscribed by reference
        /// when SectionViewsOnSheet is replaced on the next sheet switch.
        /// </summary>
        private void OnSectionViewOptionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RefreshCategories();
            OnPropertyChanged(nameof(SelectedViewsSummary));
        }

        private void OnScanCompleted(ScanEventHandler.ScanMode mode)
        {
            _dispatcher.Invoke(() =>
            {
                switch (mode)
                {
                    case ScanEventHandler.ScanMode.LoadSheets:
                        Sheets = new ObservableCollection<SheetOption>(_scanHandler.ResultSheets ?? new());
                        break;

                    case ScanEventHandler.ScanMode.LoadSectionViewsForSheet:
                        // Unsubscribe from the outgoing collection's items before
                        // replacing — without this, each sheet switch left the old
                        // SectionViewOption instances (and their lambda handlers)
                        // subscribed until GC'd, and any accidental instance reuse
                        // would double-fire RefreshCategories().
                        foreach (var old in SectionViewsOnSheet)
                            old.PropertyChanged -= OnSectionViewOptionChanged;

                        SectionViewsOnSheet = new ObservableCollection<SectionViewOption>(_scanHandler.ResultSectionViews ?? new());
                        foreach (var v in SectionViewsOnSheet)
                            v.PropertyChanged += OnSectionViewOptionChanged;

                        OnPropertyChanged(nameof(SelectedViewsSummary));
                        break;

                    case ScanEventHandler.ScanMode.ScanCategoriesForViews:
                        Categories = new ObservableCollection<CategoryTagRow>(_scanHandler.ResultCategories ?? new());
                        break;
                }
            });

            // Run any request that arrived while this scan was in flight.
            if (_queuedScanRequest != null)
            {
                var next = _queuedScanRequest;
                _queuedScanRequest = null;
                next();
            }
        }

        private void OnPlaceCompleted()
        {
            _dispatcher.Invoke(() =>
            {
                IsRunning = false;

                var result = _placeHandler.Result;
                if (result != null)
                    SummaryLine = result.ToString();

                // Auto-save log to fixed location — no folder-picker, no prompt.
                string path = _logExportHelper.SaveLog(LogEntries);
                if (path != null)
                {
                    SavedLogPath = path;
                    AppendLog(LogLevel.Info, $"Log saved to {path}");
                }
                else
                {
                    AppendLog(LogLevel.Warning, "Log auto-save failed — could not write to Documents\\Revit26_Plugin_Revised\\SectionViewAutoTagger\\Logs\\.");
                }

                // Persist current global settings after a completed run.
                _settingsService.Save(new TagPlacementSettings
                {
                    AlignmentSide = AlignmentSide,
                    OffsetMm = OffsetMm,
                    SpacingMm = SpacingMm,
                    LeaderEndCondition = LeaderEndCondition
                });

                RunCommand.NotifyCanExecuteChanged();
            });
        }

        [RelayCommand]
        private void CopyAll()
        {
            if (LogEntries.Count == 0) return;
            string text = string.Join(Environment.NewLine, LogEntries.Select(e => e.ToString()));
            System.Windows.Clipboard.SetText(text);
        }

        [RelayCommand]
        private void CopySelected()
        {
            if (SelectedLogEntries.Count == 0) return;
            string text = string.Join(Environment.NewLine, SelectedLogEntries.Select(e => e.ToString()));
            System.Windows.Clipboard.SetText(text);
        }

        [RelayCommand]
        private void ExportLog()
        {
            if (LogEntries.Count == 0) return;

            string path = _logExportHelper.SaveLog(LogEntries);
            if (path != null)
            {
                SavedLogPath = path;
                AppendLog(LogLevel.Info, $"Log re-exported to {path}");
            }
            else
            {
                AppendLog(LogLevel.Warning, "Manual log export failed.");
            }
        }

        private void AppendLog(LogLevel level, string message)
        {
            void Add() => LogEntries.Add(new LogEntry(level, message));

            if (_dispatcher.CheckAccess())
                Add();
            else
                _dispatcher.Invoke(Add);
        }
    }
}
