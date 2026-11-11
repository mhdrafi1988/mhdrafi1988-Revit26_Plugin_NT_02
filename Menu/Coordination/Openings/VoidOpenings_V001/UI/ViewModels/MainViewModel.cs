using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;
using Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Files;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.Services;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Void Openings window. It binds the panels and buttons to the use cases; the
    /// rules, geometry and Revit work live in the use cases and services it calls.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private const string JsonFilter = "JSON file (*.json)|*.json";
        private const string ExcelFilter = "Excel workbook (*.xlsx)|*.xlsx";

        private readonly ModelSelectionService _models;
        private readonly AnalyzeClashesUseCase _analyze;
        private readonly GenerateVoidsUseCase _generate;
        private readonly IViewNavigator _navigator;
        private readonly IRevitDispatcher _dispatcher;
        private readonly VoidOpeningsFiles _files;
        private readonly FileDialogService _dialogs;
        private readonly List<ResultRowViewModel> _allRows = new List<ResultRowViewModel>();

        private AnalysisResult _analysis;
        private CancellationTokenSource _cancellation;
        private ResultRowViewModel _selectedRow;
        private string _notification = string.Empty;
        private bool _notificationIsError;
        private string _statusLine = "Reading the project…";
        private string _filter = string.Empty;
        private bool _isBusy;
        private bool _canCancel;
        private double _progress;
        private bool _progressIsIndeterminate;

        /// <summary>Creates the view model.</summary>
        public MainViewModel(ModelSelectionService models, AnalyzeClashesUseCase analyze, GenerateVoidsUseCase generate,
            IViewNavigator navigator, IRevitDispatcher dispatcher, VoidOpeningsFiles files, FileDialogService dialogs)
        {
            _models = models ?? throw new ArgumentNullException(nameof(models));
            _analyze = analyze ?? throw new ArgumentNullException(nameof(analyze));
            _generate = generate ?? throw new ArgumentNullException(nameof(generate));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

            Structural = new ModelPanelViewModel("STRUCTURAL MODEL", "Structural link", _models.GetStructuralCountsAsync, ShowError);
            Mep = new ModelPanelViewModel("MEP MODEL", "MEP link", _models.GetMepCountsAsync, ShowError);
            Clearance = new ClearanceViewModel();
            Clearance.Load(_files.LoadSettings(out string warning));
            if (!string.IsNullOrEmpty(warning))
                ShowError(warning);

            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
            AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => CanCancel);
            GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy && _allRows.Any(r => r.Opening != null));
            ExportExcelCommand = new RelayCommand(() => Export(true), () => !IsBusy && _allRows.Count > 0);
            ExportJsonCommand = new RelayCommand(() => Export(false), () => !IsBusy && _allRows.Count > 0);
            ExportPackageCommand = new RelayCommand(ExportPackage, () => !IsBusy && _analysis != null);
            ApplyPackageCommand = new AsyncRelayCommand(ApplyPackageAsync, () => !IsBusy);
            ApproveAllCommand = new RelayCommand(() => SetApproved(true), () => _allRows.Any(r => r.CanApprove));
            RejectAllCommand = new RelayCommand(() => SetApproved(false), () => _allRows.Any(r => r.CanApprove));
            ZoomCommand = new AsyncRelayCommand(ZoomAsync, () => !IsBusy && SelectedRow != null && SelectedRow.HasLocation);
            HighlightMepCommand = new AsyncRelayCommand(() => HighlightAsync(true), () => !IsBusy && SelectedRow?.Opening != null);
            HighlightStructureCommand = new AsyncRelayCommand(() => HighlightAsync(false), () => !IsBusy && SelectedRow?.Opening != null);
            DismissNotificationCommand = new RelayCommand(() => Notification = string.Empty);
        }

        /// <summary>Window title, with the tool version from <see cref="ToolCatalog"/>.</summary>
        public string Title => ToolCatalog.VoidOpenings.Title;

        /// <summary>Left panel.</summary>
        public ModelPanelViewModel Structural { get; }

        /// <summary>Right panel.</summary>
        public ModelPanelViewModel Mep { get; }

        /// <summary>Clearance rules and options.</summary>
        public ClearanceViewModel Clearance { get; }

        /// <summary>Rows shown in the results table (after the Find filter).</summary>
        public ObservableCollection<ResultRowViewModel> Rows { get; } = new ObservableCollection<ResultRowViewModel>();

        /// <summary>Reads the links of the active project again.</summary>
        public IAsyncRelayCommand RefreshCommand { get; }
        /// <summary>Finds and sizes the openings.</summary>
        public IAsyncRelayCommand AnalyzeCommand { get; }
        /// <summary>Stops a running analysis.</summary>
        public IRelayCommand CancelCommand { get; }
        /// <summary>Places and cuts the approved openings in the active model.</summary>
        public IAsyncRelayCommand GenerateCommand { get; }
        /// <summary>Exports the results table to Excel.</summary>
        public IRelayCommand ExportExcelCommand { get; }
        /// <summary>Exports the results table to JSON.</summary>
        public IRelayCommand ExportJsonCommand { get; }
        /// <summary>Writes the approved openings as a package for the structural file.</summary>
        public IRelayCommand ExportPackageCommand { get; }
        /// <summary>Places the openings of a package in the active (structural) model.</summary>
        public IAsyncRelayCommand ApplyPackageCommand { get; }
        /// <summary>Ticks every opening that can be generated.</summary>
        public IRelayCommand ApproveAllCommand { get; }
        /// <summary>Clears every tick.</summary>
        public IRelayCommand RejectAllCommand { get; }
        /// <summary>Zooms the active view to the selected opening.</summary>
        public IAsyncRelayCommand ZoomCommand { get; }
        /// <summary>Selects the MEP element of the selected row.</summary>
        public IAsyncRelayCommand HighlightMepCommand { get; }
        /// <summary>Selects the structural element of the selected row.</summary>
        public IAsyncRelayCommand HighlightStructureCommand { get; }
        /// <summary>Hides the notification strip.</summary>
        public IRelayCommand DismissNotificationCommand { get; }

        /// <summary>The row selected in the results table.</summary>
        public ResultRowViewModel SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                    RaiseCommands();
            }
        }

        /// <summary>Text of the Find box: Job ID, clash id, element id, status or category.</summary>
        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value ?? string.Empty))
                    ApplyFilter();
            }
        }

        /// <summary>Summary shown in the results header.</summary>
        public string ResultsSummary
        {
            get
            {
                if (_allRows.Count == 0) return "No results yet";
                return $"{_allRows.Count} opening(s) · {_allRows.Count(r => r.IsApproved)} approved · " +
                       $"{_allRows.Count(r => r.Status == nameof(OpeningStatus.Review))} to review · " +
                       $"{_allRows.Count(r => r.Status == nameof(OpeningStatus.Cut))} cut";
            }
        }

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
        public bool NotificationIsError { get => _notificationIsError; private set => SetProperty(ref _notificationIsError, value); }

        /// <summary>Text of the status bar.</summary>
        public string StatusLine { get => _statusLine; private set => SetProperty(ref _statusLine, value); }

        /// <summary>Progress of the running step, 0..100.</summary>
        public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }

        /// <summary>True while the amount of work of the running step is not known.</summary>
        public bool ProgressIsIndeterminate { get => _progressIsIndeterminate; private set => SetProperty(ref _progressIsIndeterminate, value); }

        /// <summary>True while a step is running; the buttons are disabled meanwhile.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                if (!value)
                {
                    Progress = 0;
                    ProgressIsIndeterminate = false;
                }
                RaiseCommands();
            }
        }

        /// <summary>True while the running step can be cancelled.</summary>
        public bool CanCancel
        {
            get => _canCancel;
            private set
            {
                if (SetProperty(ref _canCancel, value))
                    CancelCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>First read of the project; called once when the window has loaded.</summary>
        public Task InitializeAsync() => RefreshAsync();

        /// <summary>Saves the clearance settings; called when the window closes.</summary>
        public void SaveSettings()
        {
            try
            {
                _files.SaveSettings(Clearance.ToSettings());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Closing must not fail because the settings folder is not writable.
            }
        }

        private async Task RefreshAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                IReadOnlyList<ModelRef> models = await _models.GetModelsAsync();
                Structural.SetModels(models);
                Mep.SetModels(models);

                int links = models.Count(m => !m.IsHost);
                int unloaded = models.Count(m => !m.IsHost && !m.IsLoaded);
                StatusLine = $"{models.FirstOrDefault(m => m.IsHost)?.DisplayName ?? "No active model"} · {links} Revit link(s)"
                    + (unloaded > 0 ? $", {unloaded} not loaded" : string.Empty);
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

        private async Task AnalyzeAsync()
        {
            VoidOpeningsSettings settings = Clearance.ToSettings();
            IReadOnlyList<string> problems = RuleValidator.Validate(settings);
            if (problems.Count > 0)
            {
                ShowError("Fix the clearance rules first. " + string.Join(" ", problems));
                return;
            }

            var request = new AnalysisRequest
            {
                Structural = Structural.SelectedModel,
                StructuralCategories = Parse<StructuralCategory>(Structural.SelectedCategoryKeys),
                Mep = Mep.SelectedModel,
                MepCategories = Parse<MepCategory>(Mep.SelectedCategoryKeys),
                Settings = settings,
            };

            _cancellation = new CancellationTokenSource();
            IsBusy = true;
            CanCancel = true;
            ProgressIsIndeterminate = true;
            var progress = new Progress<AnalysisProgress>(p =>
            {
                StatusLine = p.Stage;
                ProgressIsIndeterminate = p.Fraction < 0;
                if (p.Fraction >= 0) Progress = p.Fraction * 100;
            });

            try
            {
                SaveSettings();
                _analysis = await _analyze.RunAsync(request, progress, _cancellation.Token);

                _allRows.Clear();
                _allRows.AddRange(_analysis.Openings.Select(o => new ResultRowViewModel(o)));
                _allRows.AddRange(_analysis.StoredOnly.Select(o => new ResultRowViewModel(ExportRow.FromStored(o))));
                foreach (ResultRowViewModel row in _allRows)
                    row.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ResultRowViewModel.IsApproved)) OnPropertyChanged(nameof(ResultsSummary)); };
                ApplyFilter();

                StatusLine = $"Analysed {_analysis.MepElementCount:N0} MEP and {_analysis.StructuralElementCount:N0} structural element(s).";
                string message = $"Analyze finished: {_analysis.Openings.Count} opening(s) found"
                    + StoredSummary(_analysis.StoredOnly) + ".";
                if (!request.Structural.IsHost && _analysis.Openings.Count > 0)
                    message += " The structural model is a link, so Revit cannot cut it from this project: use Export ▸ Opening package, then Apply package in the structural file.";
                ShowInfo(Join(message, _analysis.Notes));
            }
            catch (OperationCanceledException)
            {
                StatusLine = "Analysis cancelled.";
                ShowInfo("Analysis cancelled. The previous results are unchanged.");
            }
            catch (Exception ex)
            {
                StatusLine = "Analysis failed.";
                ShowError(ex.Message);
            }
            finally
            {
                CanCancel = false;
                IsBusy = false;
                _cancellation.Dispose();
                _cancellation = null;
            }
        }

        private async Task GenerateAsync()
        {
            if (_analysis == null)
            {
                ShowError("Press Analyze first.");
                return;
            }
            if (!_analysis.Request.Structural.IsHost)
            {
                ShowError("The structural model is a link, and Revit cannot change a linked model from this project. " +
                          "Use Export ▸ Opening package, open the structural file, and press Apply package there.");
                return;
            }

            List<ResultRowViewModel> approved = _allRows.Where(r => r.IsApproved && r.Opening != null).ToList();
            if (approved.Count == 0)
            {
                ShowError("No opening is approved. Tick the openings to generate in the first column.");
                return;
            }

            IsBusy = true;
            ProgressIsIndeterminate = true;
            StatusLine = $"Generating {approved.Count} void(s)…";
            try
            {
                GenerateSummary summary = await _generate.RunAsync(approved.Select(r => r.Opening).ToList(),
                    _analysis.StructuralToHost, _analysis.Request.Settings);
                foreach (ResultRowViewModel row in approved)
                    row.Refresh();
                OnPropertyChanged(nameof(ResultsSummary));
                StatusLine = "Generate finished: " + summary.ToSentence();
                Show(Join("Generate finished: " + summary.ToSentence(), summary.Notes), summary.Failed > 0);
            }
            catch (Exception ex)
            {
                StatusLine = "Generate failed.";
                ShowError(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExportPackage()
        {
            List<OpeningData> approved = _allRows.Where(r => r.IsApproved && r.Opening != null).Select(r => r.Opening).ToList();
            if (approved.Count == 0)
            {
                ShowError("No opening is approved. Tick the openings to put in the package.");
                return;
            }

            ModelRef structural = _analysis.Request.Structural;
            string path = _dialogs.AskSave("Export opening package", JsonFilter, "VoidOpenings_Package.json");
            if (path == null) return;
            try
            {
                _files.WritePackage(path, OpeningPackageBuilder.Build(structural, _analysis.Request.Mep, approved, _analysis.StructuralToHost));
                ShowInfo($"Wrote {approved.Count} opening(s) to {path}. Open {structural.DisplayName}, start Void Openings there and press Apply package.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowError($"The package could not be written: {ex.Message}");
            }
        }

        private async Task ApplyPackageAsync()
        {
            string path = _dialogs.AskOpen("Apply opening package", JsonFilter);
            if (path == null) return;

            OpeningPackage package;
            try
            {
                package = _files.ReadPackage(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                ShowError(ex.Message);
                return;
            }

            ModelRef active = Structural.Models.FirstOrDefault(m => m.IsHost);
            bool otherModel = active != null && !string.Equals(active.ModelKey, package.StructuralModelKey, StringComparison.Ordinal);

            IsBusy = true;
            ProgressIsIndeterminate = true;
            StatusLine = $"Applying {package.Openings.Count} opening(s) from the package…";
            try
            {
                GenerateSummary summary = await _generate.ApplyPackageAsync(package, Clearance.ToSettings());
                _analysis = null;
                _allRows.Clear();
                for (int i = 0; i < package.Openings.Count && i < summary.Outcomes.Count; i++)
                    _allRows.Add(new ResultRowViewModel(RowOf(package.Openings[i], summary.Outcomes[i])));
                ApplyFilter();

                string message = "Package applied: " + summary.ToSentence();
                if (otherModel)
                    message += $" Note: the package was made for {package.StructuralModelName}, which is not the active model; openings whose element is not here failed.";
                StatusLine = message;
                Show(Join(message, summary.Notes), summary.Failed > 0 || otherModel);
            }
            catch (Exception ex)
            {
                StatusLine = "Apply package failed.";
                ShowError(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static ExportRow RowOf(VoidPlacement p, PlacementOutcome outcome) => new ExportRow
        {
            ClashId = p.ClashId, JobId = outcome.JobId ?? string.Empty,
            StructuralModel = p.StructuralModelName,
            StructuralCategory = Enum.TryParse(p.StructuralCategory, out StructuralCategory s) ? CategoryCatalog.NameOf(s) : p.StructuralCategory,
            StructuralElementId = p.StructuralElementId,
            MepModel = p.MepModelName,
            MepCategory = Enum.TryParse(p.MepCategory, out MepCategory m) ? CategoryCatalog.NameOf(m) : p.MepCategory,
            MepElementId = p.MepElementId,
            WidthMm = p.WidthMm, HeightMm = p.HeightMm, DiameterMm = p.DiameterMm, ClearanceMm = p.ClearanceMm, DepthMm = p.DepthMm,
            Status = outcome.Status == "Existing" ? "Existing" : outcome.Status,
            Tracking = outcome.Status == "Failed" ? "New" : "Existing",
            X = p.CenterX, Y = p.CenterY, Z = p.CenterZ,
            Note = outcome.Message,
        };

        private void Export(bool excel)
        {
            string path = excel
                ? _dialogs.AskSave("Export results to Excel", ExcelFilter, "VoidOpenings_Results.xlsx")
                : _dialogs.AskSave("Export results to JSON", JsonFilter, "VoidOpenings_Results.json");
            if (path == null) return;
            try
            {
                List<ExportRow> rows = _allRows.Select(r => r.Row).ToList();
                if (excel) _files.WriteResultsExcel(path, rows);
                else _files.WriteResultsJson(path, rows);
                ShowInfo($"Exported {rows.Count} row(s) to {path}.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowError($"The export could not be written: {ex.Message} Close the file if it is open in another program.");
            }
        }

        private async Task ZoomAsync()
        {
            OpeningData opening = SelectedRow?.Opening;
            if (opening == null) return;
            double size = Math.Max(Math.Max(opening.WidthMm, opening.HeightMm), Math.Max(opening.DiameterMm, opening.DepthMm)) * 4;
            await RunInRevitAsync(() => { _navigator.ZoomTo(opening.Center, size); return true; }, "The view could not be zoomed");
        }

        private async Task HighlightAsync(bool mep)
        {
            ClashData clash = SelectedRow?.Opening?.Clash;
            if (clash == null) return;
            bool found = await RunInRevitAsync(
                () => mep ? _navigator.Select(clash.MepModel, clash.Mep.UniqueId) : _navigator.Select(clash.StructuralModel, clash.Structural.UniqueId),
                "The element could not be selected");
            if (!found && !HasNotification)
                ShowError("The element was not found. Press Analyze again; it may have been deleted or its link unloaded.");
        }

        private async Task<bool> RunInRevitAsync(Func<bool> work, string failure)
        {
            try
            {
                return await _dispatcher.RunAsync(work);
            }
            catch (Exception ex)
            {
                ShowError($"{failure}: {ex.Message}");
                return false;
            }
        }

        private void SetApproved(bool approved)
        {
            foreach (ResultRowViewModel row in _allRows)
                row.IsApproved = approved;
            OnPropertyChanged(nameof(ResultsSummary));
        }

        private void ApplyFilter()
        {
            Rows.Clear();
            foreach (ResultRowViewModel row in _allRows.Where(r => r.Matches(_filter)))
                Rows.Add(row);
            OnPropertyChanged(nameof(ResultsSummary));
            RaiseCommands();
        }

        private void RaiseCommands()
        {
            RefreshCommand.NotifyCanExecuteChanged();
            AnalyzeCommand.NotifyCanExecuteChanged();
            GenerateCommand.NotifyCanExecuteChanged();
            ExportExcelCommand.NotifyCanExecuteChanged();
            ExportJsonCommand.NotifyCanExecuteChanged();
            ExportPackageCommand.NotifyCanExecuteChanged();
            ApplyPackageCommand.NotifyCanExecuteChanged();
            ApproveAllCommand.NotifyCanExecuteChanged();
            RejectAllCommand.NotifyCanExecuteChanged();
            ZoomCommand.NotifyCanExecuteChanged();
            HighlightMepCommand.NotifyCanExecuteChanged();
            HighlightStructureCommand.NotifyCanExecuteChanged();
        }

        private static string StoredSummary(IReadOnlyList<StoredOpeningCheck> stored)
        {
            int cut = stored.Count(s => s.State == TrackingState.Existing);
            int moved = stored.Count(s => s.State == TrackingState.Updated);
            int gone = stored.Count(s => s.State == TrackingState.SourceDeleted);
            var parts = new List<string>();
            if (cut > 0) parts.Add($"{cut} already cut");
            if (moved > 0) parts.Add($"{moved} whose MEP moved");
            if (gone > 0) parts.Add($"{gone} whose clash is gone");
            return parts.Count == 0 ? string.Empty : "; existing openings: " + string.Join(", ", parts);
        }

        private static IReadOnlyList<TEnum> Parse<TEnum>(IEnumerable<string> keys) where TEnum : struct
            => keys.Select(k => Enum.TryParse(k, out TEnum value) ? (TEnum?)value : null).Where(v => v.HasValue).Select(v => v.Value).ToList();

        private static string Join(string message, IEnumerable<string> notes)
        {
            List<string> extra = (notes ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            return extra.Count == 0 ? message : message + " " + string.Join(" ", extra);
        }

        private void Show(string message, bool isError)
        {
            NotificationIsError = isError;
            Notification = message;
        }

        private void ShowError(string message) => Show(message, true);

        private void ShowInfo(string message) => Show(message, false);
    }
}
