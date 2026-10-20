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
using System.Windows;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.TrackerId.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Tracker ID window: a four-step wizard. Step 1 picks a category, step 2 its
    /// types, step 3 reads each element's Tracker_ID and builds the missing ones, step 4 exports the
    /// Power BI table. The steps live in the partial files next to this one. Every model read and write
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

        /// <summary>
        /// Creates the view model for <paramref name="doc"/> and lists its categories. Must be created in a
        /// Revit API context (while the command runs): it reads the model and creates the external event.
        /// </summary>
        public TrackerIdViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _dispatcher = Dispatcher.CurrentDispatcher;
            Confirm = AskWithoutOwner;

            _settings = SettingsService<TrackerIdSettings>.Load(SettingsName) ?? new TrackerIdSettings();
            _settings.Segments = SegmentCatalog.Normalize(_settings.Segments);

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

            // Safe here: the command is still running, so this is a Revit API context.
            ShowCategories(ModelReader.ReadCategories(_doc));
            AddLog(LogLevel.Info, $"Model '{_doc.Title}': {Categories.Count} trackable categories.");
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

        /// <summary>True when step 2 can be opened (a category with elements is chosen).</summary>
        public bool CanOpenStep2 => CanOpenStep(2);

        /// <summary>True when steps 3 and 4 can be opened (at least one type is ticked).</summary>
        public bool CanOpenStep3 => CanOpenStep(3);

        /// <summary>Hint shown at the left of the footer for the current step.</summary>
        public string StepHint => CurrentStep switch
        {
            1 => "Pick the category to track. Double-click a row to go on.",
            2 => "Tick the types whose elements should get a Tracker ID.",
            3 => "Existing IDs are never changed. Write saves new IDs in one step; Ctrl+Z in Revit undoes it.",
            _ => "One row per element. Power BI joins your status list to this file on Tracker_ID."
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

        private bool CanGoNext() => CurrentStep < 4 && CanOpenStep(CurrentStep + 1);

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
                2 => SelectedCategory != null && SelectedCategory.ElementCount > 0,
                3 or 4 => SelectedTypeCount > 0,
                _ => false
            };
        }

        private void RaiseStepFlags()
        {
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(IsStep4));
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
        }

        /// <summary>Reads the categories again and forgets everything read so far.</summary>
        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private void Refresh()
        {
            Run("Reading categories…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;
                var categories = ModelReader.ReadCategories(doc);
                OnUi(() =>
                {
                    ClearTypeCache();
                    InvalidateSnapshots();
                    ShowCategories(categories);
                    CurrentStep = 1;
                    AddLog(LogLevel.Info, $"Read {categories.Count} categories again.");
                });
            });
        }

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
