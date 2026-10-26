using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.TagToText.V001.Core.Models;
using Revit26_Plugin_Revised.TagToText.V001.Core.Services;
using Revit26_Plugin_Revised.TagToText.V001.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.TagToText.V001.UI.ViewModels
{
    /// <summary>A text note type in the Text Type list.</summary>
    public sealed record TextTypeItem(ElementId Id, string Name);

    /// <summary>
    /// View model of the Tag To Text window: lists tags, holds the options (remembered between runs)
    /// and runs the copy through an external event, showing progress as it goes.
    /// </summary>
    public partial class TagToTextViewModel : ObservableObject
    {
        private const string SettingsKey = "TagToText";

        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly ElementId _viewId;
        private readonly TagToTextHandler _handler = new();
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher;
        private readonly TagToTextSettings _settings;
        private bool _cancelRequested;

        /// <summary>Creates the view model for the active view of <paramref name="uidoc"/> and lists its tags.</summary>
        public TagToTextViewModel(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _viewId = _doc.ActiveView.Id;
            ViewName = _doc.ActiveView.Name;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _externalEvent = ExternalEvent.Create(_handler);

            _settings = SettingsService<TagToTextSettings>.Load(SettingsKey).Migrate();
            ApplySettings(_settings);
            LoadTextTypes();

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = o => o is TagRow r && IsKindShown(r.Snapshot.Kind);

            LoadTags(_uidoc.Selection.GetElementIds(), SourceIsSelection);
        }

        // ── Table ────────────────────────────────────────────────────────────

        /// <summary>Tags found.</summary>
        public ObservableCollection<TagRow> Rows { get; } = new();

        /// <summary><see cref="Rows"/> filtered by the ticked categories.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>Run log, newest first.</summary>
        public ObservableCollection<LogEntry> Log { get; } = new();

        /// <summary>Text note types in the project.</summary>
        public ObservableCollection<TextTypeItem> TextTypes { get; } = new();

        /// <summary>Name of the view the tags are read from.</summary>
        public string ViewName { get; }

        /// <summary>Rows shown.</summary>
        [ObservableProperty] private int foundCount;

        /// <summary>Rows shown and ticked.</summary>
        [ObservableProperty] private int selectedCount;

        // ── Source ───────────────────────────────────────────────────────────

        /// <summary>List the selected tags.</summary>
        [ObservableProperty] private bool sourceIsSelection;

        /// <summary>List every tag in the active view.</summary>
        [ObservableProperty] private bool sourceIsView;

        /// <summary>Show Spot Elevations.</summary>
        [ObservableProperty] private bool includeSpotElevation;

        /// <summary>Show Spot Coordinates.</summary>
        [ObservableProperty] private bool includeSpotCoordinate;

        /// <summary>Show Spot Slopes.</summary>
        [ObservableProperty] private bool includeSpotSlope;

        /// <summary>Show element tags.</summary>
        [ObservableProperty] private bool includeElementTags;

        /// <summary>Show Room / Area / Space tags.</summary>
        [ObservableProperty] private bool includeSpatialTags;

        // ── Text output ──────────────────────────────────────────────────────

        /// <summary>Text type for the new notes.</summary>
        [ObservableProperty] private TextTypeItem selectedTextType;

        /// <summary>Added before every value.</summary>
        [ObservableProperty] private string prefix = string.Empty;

        /// <summary>Added after every value.</summary>
        [ObservableProperty] private string suffix = string.Empty;

        /// <summary>Add a leader where the tag had one.</summary>
        [ObservableProperty] private bool keepLeader;

        /// <summary>Rotate the text like the tag.</summary>
        [ObservableProperty] private bool matchRotation;

        /// <summary>Write the tag Id and date into the note's Comments.</summary>
        [ObservableProperty] private bool addSourceNote;

        // ── Original tags ────────────────────────────────────────────────────

        /// <summary>Keep the original tags.</summary>
        [ObservableProperty] private bool keepOriginals;

        /// <summary>Hide the original tags in the view.</summary>
        [ObservableProperty] private bool hideOriginals;

        /// <summary>Delete the original tags.</summary>
        [ObservableProperty] private bool deleteOriginals;

        // ── Place text in ────────────────────────────────────────────────────

        /// <summary>Put the text in the current view.</summary>
        [ObservableProperty] private bool targetSameView;

        /// <summary>Put the text in a new duplicate of the view.</summary>
        [ObservableProperty] private bool targetNewView;

        /// <summary>Duplicate without detailing.</summary>
        [ObservableProperty] private bool duplicateWithoutDetailing;

        /// <summary>Duplicate with detailing.</summary>
        [ObservableProperty] private bool duplicateWithDetailing;

        /// <summary>Name for the new view.</summary>
        [ObservableProperty] private string newViewName;

        /// <summary>Open the new view when done.</summary>
        [ObservableProperty] private bool openNewView;

        // ── Progress ─────────────────────────────────────────────────────────

        /// <summary>True while a run (or a pick) is in progress.</summary>
        [ObservableProperty] private bool isRunning;

        /// <summary>0–100.</summary>
        [ObservableProperty] private double progressPercent;

        /// <summary>"3 of 5 · 60%".</summary>
        [ObservableProperty] private string progressText = "Ready";

        /// <summary>What the run is doing.</summary>
        [ObservableProperty] private string stepText = "Tick the tags to copy, then Create Text.";

        /// <summary>Tags turned into text in the last run.</summary>
        [ObservableProperty] private int createdCount;

        /// <summary>Tags skipped in the last run.</summary>
        [ObservableProperty] private int skippedCount;

        /// <summary>Tags that failed in the last run.</summary>
        [ObservableProperty] private int failedCount;

        /// <summary>Log panel expanded.</summary>
        [ObservableProperty] private bool isLogExpanded = true;

        /// <summary>The original-tag options only apply when the text goes in the same view.</summary>
        public bool CanChangeOriginals => TargetSameView && !IsRunning;

        /// <summary>True when the options can be edited.</summary>
        public bool IsIdle => !IsRunning;

        partial void OnSourceIsSelectionChanged(bool value) { if (value) SourceIsView = false; }
        partial void OnSourceIsViewChanged(bool value) { if (value) SourceIsSelection = false; }
        partial void OnIncludeSpotElevationChanged(bool value) => RefreshView();
        partial void OnIncludeSpotCoordinateChanged(bool value) => RefreshView();
        partial void OnIncludeSpotSlopeChanged(bool value) => RefreshView();
        partial void OnIncludeElementTagsChanged(bool value) => RefreshView();
        partial void OnIncludeSpatialTagsChanged(bool value) => RefreshView();

        partial void OnTargetSameViewChanged(bool value)
        {
            if (value) TargetNewView = false;
            OnPropertyChanged(nameof(CanChangeOriginals));
        }

        partial void OnTargetNewViewChanged(bool value)
        {
            if (value) TargetSameView = false;
            OnPropertyChanged(nameof(CanChangeOriginals));
        }

        partial void OnDuplicateWithoutDetailingChanged(bool value) { if (value) DuplicateWithDetailing = false; }
        partial void OnDuplicateWithDetailingChanged(bool value) { if (value) DuplicateWithoutDetailing = false; }
        partial void OnKeepOriginalsChanged(bool value) { if (value) { HideOriginals = false; DeleteOriginals = false; } }
        partial void OnHideOriginalsChanged(bool value) { if (value) { KeepOriginals = false; DeleteOriginals = false; } }
        partial void OnDeleteOriginalsChanged(bool value) { if (value) { KeepOriginals = false; HideOriginals = false; } }

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanChangeOriginals));
            CreateTextCommand.NotifyCanExecuteChanged();
            ScanCommand.NotifyCanExecuteChanged();
            PickTagsCommand.NotifyCanExecuteChanged();
            CancelRunCommand.NotifyCanExecuteChanged();
        }

        // ── Commands ─────────────────────────────────────────────────────────

        /// <summary>Lists the tags again, from the Revit selection or the whole view.</summary>
        [RelayCommand(CanExecute = nameof(IsIdle))]
        private void Scan()
        {
            Queue(app =>
            {
                var selection = app.ActiveUIDocument?.Selection.GetElementIds() ?? new List<ElementId>();
                LoadTags(selection, SourceIsSelection);
            });
        }

        /// <summary>Lets the user pick tags in the view, then lists them.</summary>
        [RelayCommand(CanExecute = nameof(IsIdle))]
        private void PickTags()
        {
            IsRunning = true;
            StepText = "Pick tags in the view, then click Finish on the Options Bar (Esc cancels).";
            Queue(app =>
            {
                try
                {
                    UIDocument uidoc = app.ActiveUIDocument;
                    if (uidoc == null || !uidoc.Document.Equals(_doc) || uidoc.ActiveView.Id != _viewId)
                    {
                        AddLog(LogLevel.Error, $"Go back to the view '{ViewName}' to pick its tags.");
                        return;
                    }
                    IList<Reference> picked = uidoc.Selection.PickObjects(ObjectType.Element, new TagPickFilter(_viewId),
                        "Pick tags to copy as text");
                    SetOnUi(() => SourceIsSelection = true);
                    LoadTags(picked.Select(r => r.ElementId).ToList(), true);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    AddLog(LogLevel.Info, "Pick cancelled.");
                }
                finally
                {
                    SetOnUi(() => { IsRunning = false; StepText = "Tick the tags to copy, then Create Text."; });
                }
            });
        }

        /// <summary>Ticks every copyable row shown.</summary>
        [RelayCommand]
        private void SelectAll()
        {
            foreach (var r in VisibleRows().Where(r => r.IsCopyable)) r.IsSelected = true;
        }

        /// <summary>Unticks every row shown.</summary>
        [RelayCommand]
        private void ClearSelection()
        {
            foreach (var r in VisibleRows()) r.IsSelected = false;
        }

        /// <summary>Creates the text notes for the ticked rows.</summary>
        [RelayCommand(CanExecute = nameof(CanCreate))]
        private void CreateText()
        {
            var jobs = VisibleRows().Where(r => r.IsSelected && r.IsCopyable)
                .Select(r => new TagJob { Snapshot = r.Snapshot, Lines = r.Lines }).ToList();
            if (jobs.Count == 0)
            {
                MessageBox.Show("Tick at least one tag to copy.", ToolCatalog.TagToText.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (SelectedTextType == null)
            {
                MessageBox.Show("Choose a text type.", ToolCatalog.TagToText.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TagToTextSettings options = CaptureSettings();
            if (options.Target == TargetMode.SameView && options.OriginalAction == OriginalTagAction.Delete &&
                MessageBox.Show($"Delete the {jobs.Count} original tag(s) after copying them?\n\nYou can undo this with Ctrl+Z in Revit.",
                    ToolCatalog.TagToText.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SaveSettings();
            var request = new TagToTextRequest
            {
                SourceViewId = _viewId,
                Jobs = jobs,
                TextTypeId = SelectedTextType.Id,
                Options = options,
                NewViewName = string.IsNullOrWhiteSpace(NewViewName) ? ViewName + options.NewViewSuffix : NewViewName
            };

            _cancelRequested = false;
            CreatedCount = SkippedCount = FailedCount = 0;
            ProgressPercent = 0;
            ProgressText = $"0 of {jobs.Count} · 0%";
            StepText = "Starting…";
            IsRunning = true;
            Queue(app => RunCreate(app, request));
        }

        private bool CanCreate() => !IsRunning;

        /// <summary>Stops the run after the current tag and rolls everything back.</summary>
        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void CancelRun()
        {
            _cancelRequested = true;
            StepText = "Cancelling…";
        }

        /// <summary>Copies the log to the clipboard.</summary>
        [RelayCommand]
        private void CopyLog()
        {
            if (Log.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var e in Log.Reverse()) sb.AppendLine(e.ToString());
            try { Clipboard.SetText(sb.ToString()); }
            catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy */ }
        }

        /// <summary>Opens the folder with the suite's error logs.</summary>
        [RelayCommand]
        private void OpenLogFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(ToolGuard.LogFolder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ToolGuard.LogFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, "Can't open the log folder: " + ex.Message);
            }
        }

        /// <summary>Saves the current options to the settings file.</summary>
        public void SaveSettings() => SettingsService<TagToTextSettings>.Save(SettingsKey, CaptureSettings());

        // ── Revit work ───────────────────────────────────────────────────────

        // Runs in the Revit API context.
        private void RunCreate(UIApplication app, TagToTextRequest request)
        {
            try
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null || !uidoc.Document.Equals(_doc))
                {
                    AddLog(LogLevel.Error, "The active document changed. Switch back to the document this window was opened for.");
                    return;
                }

                AddLog(LogLevel.Info, $"Copying {request.Jobs.Count} tag(s)…");
                var service = new TagToTextService(e => AddLog(e.Level, e.Message), ShowProgress, () => _cancelRequested);
                TagToTextResult result = service.Run(_doc, request);
                AddLog(result.Cancelled ? LogLevel.Warning : result.Failed > 0 ? LogLevel.Warning : LogLevel.Success, result.ToString());

                if (result.NewView != null && request.Options.OpenNewView)
                    uidoc.RequestViewChange(result.NewView);
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Run stopped and nothing was changed: {ex.Message}");
                ToolGuard.Report(GetType(), ex);
            }
            finally
            {
                SetOnUi(() =>
                {
                    IsRunning = false;
                    if (request.Options.Target == TargetMode.SameView && request.Options.OriginalAction == OriginalTagAction.Delete)
                        LoadTags(Rows.Select(r => r.Snapshot.TagId).ToList(), true, quiet: true);
                });
            }
        }

        // Called after each tag. Updates the bars, then lets WPF draw and handle the Cancel click
        // before the next tag (the run is on the UI thread).
        private void ShowProgress(TagToTextProgress p)
        {
            ProgressPercent = p.Total == 0 ? 100 : 100.0 * p.Done / p.Total;
            ProgressText = $"{p.Done} of {p.Total} · {ProgressPercent:0}%";
            if (!_cancelRequested) StepText = p.Step;
            CreatedCount = p.Created;
            SkippedCount = p.Skipped;
            FailedCount = p.Failed;
            try { _dispatcher.Invoke(() => { }, DispatcherPriority.Background); }
            catch (InvalidOperationException) { /* dispatcher suspended; skip the redraw */ }
        }

        // Reads the tags: those in ids when useIds is true, otherwise every tag in the view.
        private void LoadTags(ICollection<ElementId> ids, bool useIds, bool quiet = false)
        {
            if (!_doc.IsValidObject)
            {
                AddLog(LogLevel.Error, "The document was closed. Close this window.");
                return;
            }
            if (_doc.GetElement(_viewId) is not View view)
            {
                AddLog(LogLevel.Error, "The view this window was opened for no longer exists. Close this window.");
                return;
            }

            List<Element> elements = useIds
                ? TagReader.CollectFromIds(_doc, view, ids)
                : TagReader.CollectInView(_doc, view);

            if (useIds && elements.Count == 0 && !quiet)
                AddLog(LogLevel.Info, "No supported tags are selected in this view. Pick Tags, or choose 'All tags in active view'.");

            var rows = elements.Select(e => TagReader.Read(_doc, view, e)).Where(s => s != null).Select(s => new TagRow(s)).ToList();
            SetOnUi(() =>
            {
                foreach (var r in Rows) r.PropertyChanged -= OnRowChanged;
                Rows.Clear();
                foreach (var r in rows.OrderBy(r => r.Snapshot.Kind).ThenBy(r => r.Text))
                {
                    r.PropertyChanged += OnRowChanged;
                    Rows.Add(r);
                }
                RefreshView();
                if (!quiet) AddLog(LogLevel.Info, $"Found {rows.Count} tag(s) in '{ViewName}'.");
            });
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void Queue(Action<UIApplication> action)
        {
            _handler.Queue(action);
            ExternalEventRequest r = _externalEvent.Raise();
            if (r != ExternalEventRequest.Accepted)
            {
                AddLog(LogLevel.Error, $"Revit is busy ({r}). Finish the current command in Revit and try again.");
                IsRunning = false;
            }
        }

        private void LoadTextTypes()
        {
            var types = new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                .Select(t => new TextTypeItem(t.Id, t.Name)).OrderBy(t => t.Name).ToList();
            foreach (var t in types) TextTypes.Add(t);

            ElementId defaultId = _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            SelectedTextType = TextTypes.FirstOrDefault(t => t.Name == _settings.TextTypeName)
                ?? TextTypes.FirstOrDefault(t => t.Id == defaultId)
                ?? TextTypes.FirstOrDefault();
        }

        private bool IsKindShown(TagKind kind) => kind switch
        {
            TagKind.SpotElevation => IncludeSpotElevation,
            TagKind.SpotCoordinate => IncludeSpotCoordinate,
            TagKind.SpotSlope => IncludeSpotSlope,
            TagKind.ElementTag => IncludeElementTags,
            _ => IncludeSpatialTags
        };

        private IEnumerable<TagRow> VisibleRows() => Rows.Where(r => IsKindShown(r.Snapshot.Kind));

        private void RefreshView()
        {
            RowsView?.Refresh();
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            FoundCount = VisibleRows().Count();
            SelectedCount = VisibleRows().Count(r => r.IsSelected);
        }

        private void OnRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TagRow.IsSelected)) UpdateCounts();
        }

        private void ApplySettings(TagToTextSettings s)
        {
            SourceIsSelection = s.Source == SourceMode.Selection;
            SourceIsView = s.Source == SourceMode.ActiveView;
            IncludeSpotElevation = s.IncludeSpotElevation;
            IncludeSpotCoordinate = s.IncludeSpotCoordinate;
            IncludeSpotSlope = s.IncludeSpotSlope;
            IncludeElementTags = s.IncludeElementTags;
            IncludeSpatialTags = s.IncludeSpatialTags;
            Prefix = s.Prefix;
            Suffix = s.Suffix;
            KeepLeader = s.KeepLeader;
            MatchRotation = s.MatchRotation;
            AddSourceNote = s.AddSourceNote;
            KeepOriginals = s.OriginalAction == OriginalTagAction.Keep;
            HideOriginals = s.OriginalAction == OriginalTagAction.Hide;
            DeleteOriginals = s.OriginalAction == OriginalTagAction.Delete;
            TargetSameView = s.Target == TargetMode.SameView;
            TargetNewView = s.Target == TargetMode.NewView;
            DuplicateWithoutDetailing = s.Duplicate == DuplicateMode.WithoutDetailing;
            DuplicateWithDetailing = s.Duplicate == DuplicateMode.WithDetailing;
            NewViewName = ViewName + s.NewViewSuffix;
            OpenNewView = s.OpenNewView;
        }

        private TagToTextSettings CaptureSettings()
        {
            string suffix = _settings.NewViewSuffix;
            if (!string.IsNullOrWhiteSpace(NewViewName) && NewViewName.StartsWith(ViewName, StringComparison.Ordinal))
                suffix = NewViewName.Substring(ViewName.Length);

            return new TagToTextSettings
            {
                Source = SourceIsView ? SourceMode.ActiveView : SourceMode.Selection,
                IncludeSpotElevation = IncludeSpotElevation,
                IncludeSpotCoordinate = IncludeSpotCoordinate,
                IncludeSpotSlope = IncludeSpotSlope,
                IncludeElementTags = IncludeElementTags,
                IncludeSpatialTags = IncludeSpatialTags,
                TextTypeName = SelectedTextType?.Name ?? string.Empty,
                Prefix = Prefix ?? string.Empty,
                Suffix = Suffix ?? string.Empty,
                KeepLeader = KeepLeader,
                MatchRotation = MatchRotation,
                AddSourceNote = AddSourceNote,
                OriginalAction = DeleteOriginals ? OriginalTagAction.Delete : HideOriginals ? OriginalTagAction.Hide : OriginalTagAction.Keep,
                Target = TargetNewView ? TargetMode.NewView : TargetMode.SameView,
                Duplicate = DuplicateWithDetailing ? DuplicateMode.WithDetailing : DuplicateMode.WithoutDetailing,
                NewViewSuffix = suffix,
                OpenNewView = OpenNewView
            };
        }

        private void AddLog(LogLevel level, string message)
            => SetOnUi(() => Log.Insert(0, new LogEntry(level, message)));

        private void SetOnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }

        /// <summary>Lets only supported tags in the source view be picked.</summary>
        private sealed class TagPickFilter : ISelectionFilter
        {
            private readonly ElementId _viewId;

            public TagPickFilter(ElementId viewId) => _viewId = viewId;

            public bool AllowElement(Element elem) => elem.OwnerViewId == _viewId && TagReader.Classify(elem) != null;

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
