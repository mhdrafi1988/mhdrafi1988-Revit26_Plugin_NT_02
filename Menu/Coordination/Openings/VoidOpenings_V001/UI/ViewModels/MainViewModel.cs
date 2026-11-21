using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Placement;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Reporting;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Files;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.Services;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Void Openings window: the Setup tab (two model panels and the clearance
    /// card), the Results tab with Generate Void and the exports, the Analyze bar, the notification
    /// strip and the status line.
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
        private readonly VoidPlacementService _placement;
        private readonly VoidOpeningsFiles _files;
        private readonly FileDialogService _dialogs;
        private AnalysisResult _lastResult;
        private ModelRef _hostModel;
        private bool _isGenerating;
        private string _generateHint = string.Empty;

        /// <summary>Creates the view model.</summary>
        public MainViewModel(ModelSelectionService models, ClearanceConfigurationService clearance,
            AnalyzeClashesUseCase analyze, NavigationService navigation, VoidPlacementService placement,
            VoidOpeningsFiles files, FileDialogService dialogs)
        {
            _analyze = analyze ?? throw new ArgumentNullException(nameof(analyze));
            _models = models ?? throw new ArgumentNullException(nameof(models));
            _placement = placement ?? throw new ArgumentNullException(nameof(placement));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            if (clearance == null) throw new ArgumentNullException(nameof(clearance));
            Clearance = new ClearanceRulesViewModel(clearance, ShowError, ShowInfo);
            Structural = new ModelPanelViewModel("STRUCTURAL MODEL", "Structural link", _models.GetStructuralCountsAsync, ShowError);
            Mep = new ModelPanelViewModel("MEP MODEL", "MEP link", _models.GetMepCountsAsync, ShowError);
            Results = new ResultsViewModel(navigation, ShowError);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsAnalyzing);
            AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsAnalyzing && BuildRequest().Problem() == null);
            CancelAnalyzeCommand = new RelayCommand(CancelAnalysis, () => IsAnalyzing);
            GenerateCommand = new AsyncRelayCommand(GenerateAsync, CanGenerate);
            ExportPackageCommand = new AsyncRelayCommand(ExportPackageAsync, () => !IsGenerating && GenerateProblem() == null);
            ApplyPackageCommand = new AsyncRelayCommand(ApplyPackageAsync, () => !IsGenerating && !IsAnalyzing);
            ExportResultsCommand = new AsyncRelayCommand(ExportResultsAsync, () => !IsGenerating && Results.HasRows);

            // Ticking a row changes what Generate and the exports would do.
            Results.PropertyChanged += OnAnalysisInputChanged;

            // Whatever changes the selection or the rules can change whether Analyze may run.
            Structural.PropertyChanged += OnAnalysisInputChanged;
            Mep.PropertyChanged += OnAnalysisInputChanged;
            Clearance.PropertyChanged += OnAnalysisInputChanged;
            UpdateAnalyzeState();
            UpdateGenerateState();
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

        /// <summary>
        /// Places a void for every ticked row in the active model and cuts the structure where Revit
        /// allows it. This changes the model.
        /// </summary>
        public IAsyncRelayCommand GenerateCommand { get; }

        /// <summary>
        /// Writes the ticked rows as an opening package, in the structural model's own coordinates,
        /// for a structure that is a Revit link.
        /// </summary>
        public IAsyncRelayCommand ExportPackageCommand { get; }

        /// <summary>Places the openings of a package in the active model. This changes the model.</summary>
        public IAsyncRelayCommand ApplyPackageCommand { get; }

        /// <summary>Writes the results table as an Excel workbook or as JSON.</summary>
        public IAsyncRelayCommand ExportResultsCommand { get; }

        /// <summary>True while voids are being placed.</summary>
        public bool IsGenerating
        {
            get => _isGenerating;
            private set
            {
                if (!SetProperty(ref _isGenerating, value))
                    return;
                AnalyzeCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                UpdateGenerateState();
            }
        }

        /// <summary>What Generate would do, or why it cannot run; shown next to the button.</summary>
        public string GenerateHint
        {
            get => _generateHint;
            private set => SetProperty(ref _generateHint, value ?? string.Empty);
        }

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

        private void OnAnalysisInputChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateAnalyzeState();
            UpdateGenerateState();
        }

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
                _lastResult = result;
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
                UpdateGenerateState();
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
                _hostModel = host;
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

        private bool CanGenerate()
            => !IsAnalyzing && !IsGenerating && GenerateProblem() == null && _lastResult.Request.Structural.IsHost;

        /// <summary>Why Generate and Export package cannot run, in the user's words; null when they can.</summary>
        private string GenerateProblem()
        {
            if (_lastResult == null) return "Press ANALYZE first.";
            if (Results.ApprovedCount == 0) return "Tick at least one row in the RESULTS table.";
            return null;
        }

        private void UpdateGenerateState()
        {
            GenerateCommand.NotifyCanExecuteChanged();
            ExportPackageCommand.NotifyCanExecuteChanged();
            ApplyPackageCommand.NotifyCanExecuteChanged();
            ExportResultsCommand.NotifyCanExecuteChanged();
            if (IsGenerating)
                return;

            string problem = GenerateProblem();
            if (problem != null)
            {
                GenerateHint = problem;
                return;
            }

            int count = Results.ApprovedCount;
            string rows = count == 1 ? "1 ticked row" : $"{count:N0} ticked rows";
            GenerateHint = _lastResult.Request.Structural.IsHost
                ? $"{rows}: Generate Void places a void for each in the active model and cuts the structure."
                : $"The structure is the link {_lastResult.Request.Structural.DisplayName}, which this session cannot change. "
                  + $"Export package writes the {rows} for it; open that file and press Apply package there.";
        }

        private async Task GenerateAsync()
        {
            string problem = GenerateProblem();
            if (problem != null)
            {
                ShowError(problem);
                return;
            }
            if (!_lastResult.Request.Structural.IsHost)
            {
                ShowError($"The structural model is the link {_lastResult.Request.Structural.DisplayName}. Voids can only be placed in the "
                    + "active model: use Export package, open that file and press Apply package there.");
                return;
            }

            IReadOnlyList<OpeningData> openings = Results.ApprovedOpenings;
            IsGenerating = true;
            try
            {
                GenerateSummary summary = await _placement.GenerateAsync(openings, _lastResult.Request.Setup);
                Results.RefreshResults();
                ShowOutcome(summary);
            }
            catch (Exception ex)
            {
                ShowError("Generate Void stopped: " + ex.Message);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private async Task ExportPackageAsync()
        {
            string problem = GenerateProblem();
            if (problem != null)
            {
                ShowError(problem);
                return;
            }

            string path = _dialogs.AskSave("Export opening package", "Void Openings package (*.json)|*.json",
                $"VoidOpenings_{Sanitize(_lastResult.Request.Structural.DisplayName)}_{DateTime.Now:yyyyMMdd_HHmm}.json");
            if (path == null)
                return;

            IsGenerating = true;
            try
            {
                OpeningPackage package = await _placement.BuildPackageAsync(
                    _lastResult.Request.Structural, _lastResult.Request.Mep, Results.ApprovedOpenings);
                await Task.Run(() => _files.WritePackage(path, package));
                ShowInfo($"{package.Openings.Count:N0} opening(s) written to {path}. Open {package.StructuralModelName} and press Apply package there.");
            }
            catch (Exception ex)
            {
                ShowError("The package was not written: " + ex.Message);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private async Task ApplyPackageAsync()
        {
            string path = _dialogs.AskOpen("Apply opening package", "Void Openings package (*.json)|*.json");
            if (path == null)
                return;

            IsGenerating = true;
            try
            {
                OpeningPackage package = await Task.Run(() => _files.ReadPackage(path));
                if (_hostModel != null && !string.IsNullOrEmpty(package.StructuralModelKey)
                    && !string.Equals(package.StructuralModelKey, _hostModel.ModelKey, StringComparison.Ordinal))
                {
                    ShowError($"This package was written for {package.StructuralModelName}, which is not the active model. "
                        + "Open that model and apply the package there.");
                    return;
                }

                GenerateSummary summary = await _placement.ApplyPackageAsync(package, Clearance.ValidSetup);
                ShowOutcome(summary);
            }
            catch (Exception ex)
            {
                ShowError("The package was not applied: " + ex.Message);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private async Task ExportResultsAsync()
        {
            if (!Results.HasRows)
            {
                ShowError("There is nothing to export: press ANALYZE first.");
                return;
            }

            string path = _dialogs.AskSave("Export results", "Excel workbook (*.xlsx)|*.xlsx|JSON (*.json)|*.json",
                $"VoidOpenings_results_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            if (path == null)
                return;

            List<ExportRow> rows = Results.AllRows.Select(row => ExportRow.From(row.Review.Opening)).ToList();
            IsGenerating = true;
            try
            {
                bool json = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                await Task.Run(() =>
                {
                    if (json) _files.WriteResultsJson(path, rows);
                    else _files.WriteResultsExcel(path, rows);
                });
                ShowInfo($"{rows.Count:N0} row(s) written to {path}.");
            }
            catch (Exception ex)
            {
                ShowError("The results were not exported: " + ex.Message);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        /// <summary>Shows the counts of a Generate or Apply run, with the writer's notes behind them.</summary>
        private void ShowOutcome(GenerateSummary summary)
        {
            string text = summary.ToSentence();
            if (summary.Notes.Count > 0)
                text += " " + string.Join(" ", summary.Notes.Distinct());
            if (summary.Failed > 0)
                ShowError(text);
            else
                ShowInfo(text);
        }

        /// <summary>A model name as a file name: anything a path cannot hold becomes an underscore.</summary>
        private static string Sanitize(string name)
        {
            var text = new System.Text.StringBuilder();
            foreach (char character in name ?? string.Empty)
                text.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), character) >= 0 ? '_' : character);
            return text.Length == 0 ? "package" : text.ToString();
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
