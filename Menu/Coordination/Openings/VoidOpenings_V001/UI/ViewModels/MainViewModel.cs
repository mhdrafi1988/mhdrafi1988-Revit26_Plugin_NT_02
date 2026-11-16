using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Void Openings window: the Setup tab (two model panels and the clearance
    /// card), the Results tab, the Analyze bar, the notification strip and the status line.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private readonly ModelSelectionService _models;
        private string _notification = string.Empty;
        private bool _notificationIsError;
        private string _statusLine = "Reading the project…";
        private bool _isBusy;
        private readonly AnalyzeClashesUseCase _analyze;
        private CancellationTokenSource _analysisCancel;
        private bool _isAnalyzing;
        private bool _isResultsTab;
        private double _progressPercent;
        private string _progressText = string.Empty;
        private string _analyzeHint = string.Empty;

        /// <summary>Creates the view model.</summary>
        public MainViewModel(ModelSelectionService models, ClearanceConfigurationService clearance,
            AnalyzeClashesUseCase analyze, NavigationService navigation)
        {
            _analyze = analyze ?? throw new ArgumentNullException(nameof(analyze));
            _models = models ?? throw new ArgumentNullException(nameof(models));
            if (clearance == null) throw new ArgumentNullException(nameof(clearance));
            Clearance = new ClearanceRulesViewModel(clearance, ShowError, ShowInfo);
            Structural = new ModelPanelViewModel("STRUCTURAL MODEL", "Structural link", _models.GetStructuralCountsAsync, ShowError);
            Mep = new ModelPanelViewModel("MEP MODEL", "MEP link", _models.GetMepCountsAsync, ShowError);
            Results = new ResultsViewModel(navigation, ShowError);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsAnalyzing);
            AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsAnalyzing && BuildRequest().Problem() == null);
            CancelAnalyzeCommand = new RelayCommand(CancelAnalysis, () => IsAnalyzing);

            // Whatever changes the selection or the rules can change whether Analyze may run.
            Structural.PropertyChanged += OnAnalysisInputChanged;
            Mep.PropertyChanged += OnAnalysisInputChanged;
            Clearance.PropertyChanged += OnAnalysisInputChanged;
            UpdateAnalyzeState();
            DismissNotificationCommand = new RelayCommand(() => Notification = string.Empty);
        }

        /// <summary>Window title, with the tool version from <see cref="ToolCatalog"/>.</summary>
        public string Title => ToolCatalog.VoidOpenings.Title;

        /// <summary>Left panel.</summary>
        public ModelPanelViewModel Structural { get; }

        /// <summary>Right panel.</summary>
        public ModelPanelViewModel Mep { get; }

        /// <summary>The OPENING CLEARANCE card.</summary>
        public ClearanceRulesViewModel Clearance { get; }

        /// <summary>The RESULTS tab.</summary>
        public ResultsViewModel Results { get; }

        /// <summary>Finds the clashes and sizes the openings. Changes nothing in the model.</summary>
        public IAsyncRelayCommand AnalyzeCommand { get; }

        /// <summary>Stops a running Analyze; the previous results stay.</summary>
        public IRelayCommand CancelAnalyzeCommand { get; }

        /// <summary>True when the Results tab is shown, false for the Setup tab.</summary>
        public bool IsResultsTab
        {
            get => _isResultsTab;
            set
            {
                if (SetProperty(ref _isResultsTab, value))
                    OnPropertyChanged(nameof(IsSetupTab));
            }
        }

        /// <summary>True when the Setup tab is shown.</summary>
        public bool IsSetupTab
        {
            get => !_isResultsTab;
            set => IsResultsTab = !value;
        }

        /// <summary>True while Analyze runs.</summary>
        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            private set
            {
                if (!SetProperty(ref _isAnalyzing, value))
                    return;
                AnalyzeCommand.NotifyCanExecuteChanged();
                CancelAnalyzeCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                UpdateAnalyzeState();
            }
        }

        /// <summary>Progress of the running Analyze, 0 to 100.</summary>
        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetProperty(ref _progressPercent, value);
        }

        /// <summary>What the running Analyze is doing.</summary>
        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value ?? string.Empty);
        }

        /// <summary>Why Analyze cannot run, or what it will use; shown next to the button.</summary>
        public string AnalyzeHint
        {
            get => _analyzeHint;
            private set => SetProperty(ref _analyzeHint, value ?? string.Empty);
        }

        /// <summary>Reads the links of the active project again.</summary>
        public IAsyncRelayCommand RefreshCommand { get; }

        /// <summary>Hides the notification strip.</summary>
        public IRelayCommand DismissNotificationCommand { get; }

        /// <summary>Message in the notification strip; empty hides the strip.</summary>
        public string Notification
        {
            get => _notification;
            private set
            {
                if (SetProperty(ref _notification, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasNotification));
            }
        }

        /// <summary>True when the strip shows a message.</summary>
        public bool HasNotification => _notification.Length > 0;

        /// <summary>True when the message is an error, false for information.</summary>
        public bool NotificationIsError
        {
            get => _notificationIsError;
            private set => SetProperty(ref _notificationIsError, value);
        }

        /// <summary>Text of the status bar.</summary>
        public string StatusLine
        {
            get => _statusLine;
            private set => SetProperty(ref _statusLine, value);
        }

        /// <summary>True while the model list is being read.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RefreshCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// First read of the saved clearance rules and of the project; called once when the window has loaded.
        /// </summary>
        public Task InitializeAsync()
        {
            Clearance.Load();
            return RefreshAsync();
        }

        /// <summary>Stops a running Analyze. Also called when the window closes.</summary>
        public void CancelAnalysis()
        {
            try { _analysisCancel?.Cancel(); }
            catch (ObjectDisposedException) { /* the run just finished */ }
        }

        private AnalysisRequest BuildRequest() => new AnalysisRequest(
            Structural.SelectedModel, Structural.SelectedCategories<StructuralCategory>(),
            Mep.SelectedModel, Mep.SelectedCategories<MepCategory>(),
            Clearance.ValidSetup);

        private void OnAnalysisInputChanged(object sender, PropertyChangedEventArgs e) => UpdateAnalyzeState();

        private void UpdateAnalyzeState()
        {
            AnalyzeCommand.NotifyCanExecuteChanged();
            if (IsAnalyzing)
                return;
            string problem = BuildRequest().Problem();
            AnalyzeHint = problem ?? (Clearance.IsDirty
                ? "Ready. Uses the clearance rules as shown (not saved yet)."
                : "Ready. Nothing in the model is changed by Analyze.");
        }

        private async Task AnalyzeAsync()
        {
            AnalysisRequest request = BuildRequest();
            string problem = request.Problem();
            if (problem != null)
            {
                ShowError(problem);
                return;
            }

            var cancel = new CancellationTokenSource();
            _analysisCancel = cancel;
            IsAnalyzing = true;
            ProgressPercent = 0;
            ProgressText = "Starting…";
            if (HasNotification)
                Notification = string.Empty;

            try
            {
                // Created here, on the window's thread, so reports from the worker thread come back to it.
                var progress = new Progress<AnalysisProgress>(p =>
                {
                    if (!ReferenceEquals(_analysisCancel, cancel))
                        return; // a late report of a finished run
                    ProgressPercent = p.Fraction * 100;
                    ProgressText = p.Stage;
                });

                AnalysisResult result = await _analyze.RunAsync(request, progress, cancel.Token);
                Results.Show(result);
                IsResultsTab = true;
                ShowInfo(Describe(result));
            }
            catch (OperationCanceledException)
            {
                ShowInfo("Analyze was cancelled. The earlier results, if any, are unchanged.");
            }
            catch (Exception ex)
            {
                ShowError("Analyze stopped: " + ex.Message);
            }
            finally
            {
                _analysisCancel = null;
                cancel.Dispose();
                ProgressPercent = 0;
                ProgressText = string.Empty;
                IsAnalyzing = false;
            }
        }

        private static string Describe(AnalysisResult result)
        {
            string clashes = result.Openings.Count == 1 ? "1 clash" : $"{result.Openings.Count:N0} clashes";
            string text = $"Analyze finished in {result.Duration.TotalSeconds:0.0} s: {clashes} from {result.MepElementCount:N0} MEP "
                + $"and {result.StructuralElementCount:N0} nearby structural elements.";
            if (result.MepElementCount == 0)
                text += " No MEP element could be read from the ticked categories.";
            if (result.NotAnalysed.Count > 0)
                text += $" {result.NotAnalysed.Count:N0} element(s) could not be analysed; see the list under the table.";
            return text;
        }

        private async Task RefreshAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                IReadOnlyList<ModelRef> models = await _models.GetModelsAsync();
                Structural.SetModels(models);
                Mep.SetModels(models);

                int links = models.Count(m => !m.IsHost);
                int unloaded = models.Count(m => !m.IsHost && !m.IsLoaded);
                ModelRef host = models.FirstOrDefault(m => m.IsHost);
                StatusLine = $"{host?.DisplayName ?? "No active model"} · {links} Revit link(s)"
                    + (unloaded > 0 ? $", {unloaded} not loaded" : string.Empty);

                if (links == 0 && !(HasNotification && NotificationIsError))
                    ShowInfo("This project has no Revit links. You can still choose the active model on both sides.");
                else if (HasNotification && !NotificationIsError)
                    Notification = string.Empty;
            }
            catch (Exception ex)
            {
                StatusLine = "Could not read the project.";
                ShowError(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowError(string message)
        {
            NotificationIsError = true;
            Notification = message;
        }

        private void ShowInfo(string message)
        {
            NotificationIsError = false;
            Notification = message;
        }
    }
}
