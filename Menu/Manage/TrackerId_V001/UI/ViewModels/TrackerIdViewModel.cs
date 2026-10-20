using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Services;
using Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.Revit;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.TrackerId.V001.UI.ViewModels
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
        private const string SettingsName = "TrackerId";

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

            _settings = SettingsService<TrackerIdSettings>.Load(SettingsName) ?? new TrackerIdSettings();
            _settings.Segments = SegmentCatalog.Normalize(_settings.Segments);
            _settings.CategoryIds ??= new System.Collections.Generic.List<long>();
            _settings.StatusColours ??= new System.Collections.Generic.List<StatusColour>();

            _jobs = new RevitJobQueue("Tracker ID V001");
            _jobs.BusyChanged += (_, _) => OnUi(() =>
            {
                IsRunning = _jobs.IsBusy;
                RefreshCommands();
            });

            InitCategories();
            InitTypes();
            InitFormat();
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

        // ── Wizard ────────────────────────────────────────────────────────────

        /// <summary>Current step, 1–4.</summary>
        [ObservableProperty] private int _currentStep = 1;

        /// <summary>True on step 1. Setting it from the step bar opens the step.</summary>
        public bool IsStep1 { get => CurrentStep == 1; set => TryOpenStep(1, value); }

        /// <summary>True on step 2. Setting it from the step bar opens the step when allowed.</summary>
        public bool IsStep2 { get => CurrentStep == 2; set => TryOpenStep(2, value); }

        /// <summary>True on step 3. Setting it from the step bar opens the step when allowed.</summary>
        public bool IsStep3 { get => CurrentStep == 3; set => TryOpenStep(3, value); }

        /// <summary>True on step 4. Setting it from the step bar opens the step when allowed.</summary>
        public bool IsStep4 { get => CurrentStep == 4; set => TryOpenStep(4, value); }

        /// <summary>True on step 5 (status). Always available: it works on the whole model.</summary>
        public bool IsStep5 { get => CurrentStep == 5; set => TryOpenStep(5, value); }

        /// <summary>True on step 6 (health check). Always available: it works on the whole model.</summary>
        public bool IsStep6 { get => CurrentStep == 6; set => TryOpenStep(6, value); }

        /// <summary>True when step 2 can be opened (a category with elements is ticked).</summary>
        public bool CanOpenStep2 => CanOpenStep(2);

        /// <summary>True when steps 3 and 4 can be opened (at least one type is ticked).</summary>
        public bool CanOpenStep3 => CanOpenStep(3);

        /// <summary>Hint shown at the left of the footer for the current step.</summary>
        public string StepHint => CurrentStep switch
        {
            1 => "Pick the scope, then tick one or more categories. Double-click a row to go on.",
            2 => "Tick the types whose elements should get a Tracker ID.",
            3 => "Existing IDs are never changed. Write saves new IDs in one step; Ctrl+Z in Revit undoes it. Double-click a row to zoom to it.",
            4 => "One row per element. Power BI joins your status list to this file on Tracker_ID.",
            5 => "Read the team's status file, write it into Revit, then colour a view by status.",
            _ => "Checks every tracked element in the model. Double-click a row to zoom to it."
        };

        partial void OnCurrentStepChanged(int value)
        {
            RaiseStepFlags();
            OnPropertyChanged(nameof(StepHint));

            if (value == 3 || value == 4) EnsureSnapshots();
            RefreshCommands();
        }

        /// <summary>Goes to the next step.</summary>
        [RelayCommand(CanExecute = nameof(CanGoNext))]
        private void Next() => CurrentStep++;

        private bool CanGoNext() => CurrentStep < 6 && CanOpenStep(CurrentStep + 1);

        /// <summary>Goes back one step.</summary>
        [RelayCommand(CanExecute = nameof(CanGoBack))]
        private void Back() => CurrentStep--;

        private bool CanGoBack() => !IsRunning && CurrentStep > 1;

        // The step bar binds IsStepN two-way, so a click lands here; a refused click is undone by re-raising.
        private void TryOpenStep(int step, bool value)
        {
            if (value && step != CurrentStep && CanOpenStep(step)) CurrentStep = step;
            else RaiseStepFlags();
        }

        private bool CanOpenStep(int step)
        {
            if (IsRunning) return false;
            return step switch
            {
                1 => true,
                2 => TickedCategories.Count > 0,
                3 or 4 => SelectedTypeCount > 0,
                5 or 6 => true,
                _ => false
            };
        }

        private void RaiseStepFlags()
        {
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(IsStep4));
            OnPropertyChanged(nameof(IsStep5));
            OnPropertyChanged(nameof(IsStep6));
        }

        private void RefreshCommands()
        {
            OnPropertyChanged(nameof(CanOpenStep2));
            OnPropertyChanged(nameof(CanOpenStep3));
            NextCommand.NotifyCanExecuteChanged();
            BackCommand.NotifyCanExecuteChanged();
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
        private void Refresh() => ReloadCategories(captureScope: ScopeMode == ScopeMode.ActiveView, afterShow: () => CurrentStep = 1);

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
            => MessageBox.Show(question, ToolCatalog.TrackerId.Title, MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;

        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }
    }
}
