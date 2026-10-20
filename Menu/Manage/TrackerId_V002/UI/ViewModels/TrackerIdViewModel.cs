using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.TrackerId.V002.UI.ViewModels
{
    /// <summary>
    /// View model of the Tracker ID window: a six-step wizard. Step 1 picks the scope and one or more
    /// categories, step 2 their types, step 3 reads each element's Tracker_ID and builds the missing
    /// ones, step 4 exports the Power BI table and the status template, step 5 reads the team's status
    /// file back into Revit and colours views, step 6 audits the whole model. The steps live in the
    /// partial files next to this one. Every model read and write
    /// after the window opens goes through a <see cref="RevitJobQueue"/>, so it runs in a Revit API context.
    /// </summary>
    public partial class TrackerIdViewModel : ToolViewModelBase
    {
        private const string SettingsName = "TrackerId_V002";
        private const string V001SettingsName = "TrackerId";

        // V002 keeps its own settings file. The first time it opens, it starts from Tracker ID V001's
        // settings (format, segments, export path), so the user's format carries over.
        private static TrackerIdSettings LoadSettings()
        {
            string own = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                "Revit26_Plugin", SettingsName, "settings.json");
            string name = System.IO.File.Exists(own) ? SettingsName : V001SettingsName;
            return SettingsService<TrackerIdSettings>.Load(name) ?? new TrackerIdSettings();
        }

        private readonly Document _doc;
        private readonly RevitJobQueue _jobs;
        private readonly Dispatcher _dispatcher;
        private readonly TrackerIdSettings _settings;

        /// <summary>
        /// Asks the user a yes/no question and returns true for yes. The window replaces it with a dialog
        /// it owns, so the question always opens in front of the window.
        /// </summary>
        public Func<string, bool> Confirm { get; set; }

        /// <summary>Asks for the export file; gets the current path, returns the chosen one or null. Set by the window.</summary>
        public Func<string, string> PickExportPath { get; set; }

        /// <summary>Asks for the status file to read (Excel or CSV); returns the chosen path or null. Set by the window.</summary>
        public Func<string, string> PickStatusFile { get; set; }

        /// <summary>Asks where to save the status template; returns the chosen path or null. Set by the window.</summary>
        public Func<string, string> PickTemplatePath { get; set; }

        /// <summary>Asks where to save the health report; returns the chosen path or null. Set by the window.</summary>
        public Func<string, string> PickReportPath { get; set; }

        /// <summary>
        /// Creates the view model for <paramref name="uidoc"/> and lists its categories within the saved
        /// scope. Must be created in a Revit API context (while the command runs): it reads the model, the
        /// selection and the active view, and creates the external event.
        /// </summary>
        public TrackerIdViewModel(UIDocument uidoc)
        {
            if (uidoc == null) throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;
            _dispatcher = Dispatcher.CurrentDispatcher;
            Confirm = AskWithoutOwner;

            _settings = LoadSettings();
            _settings.Segments = SegmentCatalog.Normalize(_settings.Segments);
            _settings.CategoryIds ??= new System.Collections.Generic.List<long>();
            _settings.StatusColours ??= new System.Collections.Generic.List<StatusColour>();

            _jobs = new RevitJobQueue("Tracker ID V002");
            _jobs.BusyChanged += (_, _) => OnUi(() =>
            {
                IsRunning = _jobs.IsBusy;
                RefreshCommands();
            });

            InitCategories();
            InitTypes();
            InitFormat();
            InitNaming();
            InitExport();
            InitStatus();
            InitHealth();

            // Safe here: the command is still running, so this is a Revit API context.
            ScopeMode = _settings.Scope;
            _scope = CaptureScope(uidoc, _settings.Scope, out _scopeViewName);
            _restoreCategoryIds = new System.Collections.Generic.HashSet<long>(_settings.CategoryIds);
            ShowCategories(ModelReader.ReadCategories(_doc, _scope));
            AddLog(LogLevel.Info, $"Model '{_doc.Title}': {Categories.Count(c => c.ElementCount > 0)} categories with elements in {ScopeText}.");
        }

        // ── Pages ─────────────────────────────────────────────────────────────

        /// <summary>1 = Elements &amp; tracking, 2 = Tracker ID naming.</summary>
        [ObservableProperty] private int _currentPage = 1;

        /// <summary>True on page 1. The tab binds it two-way.</summary>
        public bool IsPage1 { get => CurrentPage == 1; set { if (value) CurrentPage = 1; else RaisePageFlags(); } }

        /// <summary>True on page 2 (naming). The tab binds it two-way.</summary>
        public bool IsPage2 { get => CurrentPage == 2; set { if (value) CurrentPage = 2; else RaisePageFlags(); } }

        /// <summary>Page 1 section "Result" is expanded.</summary>
        [ObservableProperty] private bool _isResultExpanded = true;

        /// <summary>Header of the Scope section, e.g. "Whole model".</summary>
        public string ScopeHeader => ScopeText;

        /// <summary>Header of the Categories &amp; types section.</summary>
        public string PickHeader => TickedCategories.Count == 0
            ? "Tick one or more categories"
            : $"{TickedCategories.Count} categor{(TickedCategories.Count == 1 ? "y" : "ies")} · {SelectedTypeCount} type(s) · {SelectedElementCount} elements";

        /// <summary>Hint shown at the left of the footer.</summary>
        public string StepHint => CurrentPage == 2
            ? "Decide how new Tracker IDs are built. Existing IDs are never changed."
            : "Existing IDs are never changed. Write IDs saves new IDs in one step; Ctrl+Z in Revit undoes it. Double-click a row to zoom to it.";

        partial void OnCurrentPageChanged(int value)
        {
            RaisePageFlags();
            OnPropertyChanged(nameof(StepHint));
            EnsureSnapshots();
            RefreshCommands();
        }

        private void RaisePageFlags()
        {
            OnPropertyChanged(nameof(IsPage1));
            OnPropertyChanged(nameof(IsPage2));
        }

        /// <summary>Opens the naming page.</summary>
        [RelayCommand]
        private void OpenNamingPage() => CurrentPage = 2;

        /// <summary>Opens the elements page.</summary>
        [RelayCommand]
        private void OpenElementsPage() => CurrentPage = 1;

        private void RefreshCommands()
        {
            OnPropertyChanged(nameof(PickHeader));
            OnPropertyChanged(nameof(ScopeHeader));
            WriteCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
            WriteStatusTemplateCommand.NotifyCanExecuteChanged();
            ReadStatusCommand.NotifyCanExecuteChanged();
            ImportStatusCommand.NotifyCanExecuteChanged();
            ColourActiveViewCommand.NotifyCanExecuteChanged();
            Colour3DViewCommand.NotifyCanExecuteChanged();
            RunHealthCheckCommand.NotifyCanExecuteChanged();
            FixInStep3Command.NotifyCanExecuteChanged();
        }

        /// <summary>Reads the categories again and forgets everything read so far.</summary>
        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private void Refresh() => ReloadCategories(captureScope: ScopeMode == ScopeMode.ActiveView, afterShow: () => CurrentPage = 1);

        private bool CanRefresh() => !IsRunning;

        /// <summary>Saves the format and export settings; called when the window closes.</summary>
        public void Shutdown()
        {
            SaveSettings();
            _jobs.Dispose();
        }

        private void SaveSettings()
        {
            _settings.Segments = CurrentFormat().Segments;
            _settings.Separator = SelectedSeparator?.Value ?? "-";
            _settings.NoZoneText = NoZoneText;
            _settings.MissingText = MissingText;
            _settings.GridToleranceM = GridToleranceM;
            _settings.UpperCase = UpperCase;
            _settings.TypeCodes = CurrentTypeCodes();
            _settings.WriteLocationParameters = WriteLocationParameters;
            _settings.WriteSourceGuid = WriteSourceGuid;
            _settings.ExportPath = ExportPath;
            _settings.ExportWholeModel = ExportWholeModel;
            _settings.ExportDatedCopy = ExportDatedCopy;
            _settings.ExportFormat = ExportFormat;
            _settings.Scope = ScopeMode;
            _settings.CategoryIds = TickedCategories.Select(c => c.Info.Id).ToList();
            _settings.StatusFilePath = StatusFilePath ?? string.Empty;
            _settings.StatusTemplatePath = StatusTemplatePath ?? string.Empty;
            _settings.StatusColours = StatusColourRows.Select(r => r.ToModel()).Where(c => c.Status.Length > 0).ToList();
            _settings.SchemaVersion = TrackerIdSettings.CurrentSchemaVersion;
            SettingsService<TrackerIdSettings>.Save(SettingsName, _settings);
        }

        // ── Revit plumbing ────────────────────────────────────────────────────

        // Queues work for the Revit API context. A failure is logged and reported, and the window never
        // stays stuck on "running".
        private void Run(string summary, Action<UIApplication> body)
        {
            SummaryText = summary;
            if (_jobs.TryRun(body, OnJobFailed)) return;

            SummaryText = "Revit is busy. Close any open dialog and try again.";
            AddLog(LogLevel.Warning, SummaryText);
        }

        private void OnJobFailed(Exception ex)
        {
            OnUi(() =>
            {
                SummaryText = "Stopped, nothing was changed: " + ex.Message;
                AddLog(LogLevel.Error, SummaryText);
            });
            ToolGuard.Report(GetType(), ex);
        }

        // The document this window was opened for, when it is still open and active. Otherwise null, with
        // the reason in the status line and the log.
        private Document DocumentFor(UIApplication app)
        {
            string problem;
            if (!_doc.IsValidObject)
            {
                problem = "The document this window was opened for has been closed. Close this window.";
            }
            else
            {
                var active = app.ActiveUIDocument?.Document;
                if (active != null && active.Equals(_doc)) return active;
                problem = $"The active document changed. Switch back to '{_doc.Title}' to continue.";
            }

            OnUi(() =>
            {
                SummaryText = problem;
                AddLog(LogLevel.Error, problem);
            });
            return null;
        }

        private static bool AskWithoutOwner(string question)
            => MessageBox.Show(question, ToolCatalog.TrackerIdV002.Title, MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;

        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }
    }
}
