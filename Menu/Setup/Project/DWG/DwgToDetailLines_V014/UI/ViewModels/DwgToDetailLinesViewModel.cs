// ==============================================
// File: DwgToDetailLinesViewModel.cs
// Layer: UI/ViewModels
// Changes vs V013 (DWG To DL(P) V014):
//   ADDED Mapping mode: Single (one global line style + one global hatch for all
//         layers) or Multiple (per-layer dropdown; untouched rows follow the
//         global values). Default entry "(Match layer name)" keeps V013 behaviour.
//   ADDED Apply-global-to-all, and mode / globals / per-layer picks remembered
//         between sessions (MappingSettingsService).
// Changes vs V010:
//   FIX  AvailableLineStyles / AvailableFillPatterns were hardcoded
//        placeholder strings, not read from the project. Now populated
//        from ProjectCatalogService (real OST_Lines subcategories /
//        FilledRegionType names) on construction and refresh.
//   FIX  Per-tool LogEntryViewModel(Message,Color) replaced with the
//        shared LogEntry(Level,Message) model, matching every other tool
//        in the suite.
//   ADDED Close/Copy Selected/Export log commands (log panel convention
//        from MasterGuide.md section 10) â€” this window previously had no
//        Close button at all (modeless, closed only via the window chrome).
// ==============================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Revit26_Plugin_Revised.DwgToDetailLines.V014.Core.Models;
using Revit26_Plugin_Revised.DwgToDetailLines.V014.Core.Services;
using Revit26_Plugin_Revised.DwgToDetailLines.V014.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DwgToDetailLines.V014.UI.ViewModels
{
    public partial class DwgToDetailLinesViewModel : ObservableObject
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _convertEvent;
        private readonly ConvertExternalEventHandler _convertHandler;

        public ObservableCollection<CadImportItem> AvailableCads { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();
        public ObservableCollection<LayerRow> LayerRows { get; } = new();

        /// <summary>Grouped view of LayerRows for the grid â€” groups by EntityType (Lines / Hatches).</summary>
        public ICollectionView LayerRowsView { get; }

        public List<string> AvailableLineStyles { get; private set; } = new();
        public List<string> AvailableFillPatterns { get; private set; } = new();

        [ObservableProperty] private CadImportItem selectedCad;
        [ObservableProperty] private SplineHandlingMode splineHandlingMode;
        [ObservableProperty] private TransformMethod transformMethod = TransformMethod.None;
        [ObservableProperty] private string contextBannerText;
        [ObservableProperty] private ConversionMetrics metrics = ConversionMetrics.Empty;
        [ObservableProperty] private bool isRunning;
        [ObservableProperty] private double progress;                       // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty] private string summaryText = string.Empty;    // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty] private string layerFilterText = string.Empty;
        [ObservableProperty] private string defaultLineStyle = LayerRow.MatchLayerName;
        [ObservableProperty] private string defaultFillPattern = LayerRow.MatchLayerName;
        [ObservableProperty] private MappingMode mappingMode = MappingMode.Single;

        /// <summary>True when each layer row has its own editable style / hatch dropdown.</summary>
        public bool IsMultipleMode => MappingMode == MappingMode.Multiple;

        private MappingSettings _saved = new();
        [ObservableProperty] private LayerRow selectedLayerRow;

        public RelayCommand ConvertCommand { get; }
        public RelayCommand ApplyGlobalToAllCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand ClearCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand CloseCommand { get; }
        public RelayCommand CopyAllLogCommand { get; }
        public RelayCommand CopySelectedLogCommand { get; }
        public RelayCommand ExportLogCommand { get; }

        /// <summary>Set by the View's SelectionChanged handler for "Copy Selected".</summary>
        public IList SelectedLogEntries { get; set; }

        public event Action RequestClose;

        public DwgToDetailLinesViewModel(UIApplication app)
        {
            _uiApp = app;

            // Must be created while on the main Revit API thread (here, during
            // window construction inside Command.Execute's valid API context).
            _convertHandler = new ConvertExternalEventHandler();
            _convertEvent = ExternalEvent.Create(_convertHandler);

            ConvertCommand = new RelayCommand(Convert, CanConvert);
            ApplyGlobalToAllCommand = new RelayCommand(ApplyGlobalToAll);
            SelectAllCommand = new RelayCommand(() => SetAllVisible(true));
            ClearCommand = new RelayCommand(() => SetAllVisible(false));
            RefreshCommand = new RelayCommand(RefreshLayers);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            CopyAllLogCommand = new RelayCommand(CopyAllLog, () => LogEntries.Count > 0);
            CopySelectedLogCommand = new RelayCommand(CopySelectedLog);
            ExportLogCommand = new RelayCommand(ExportLog);
            LogEntries.CollectionChanged += (_, _) => CopyAllLogCommand.NotifyCanExecuteChanged();

            RefreshCatalog();
            LoadSavedMapping();

            LayerRowsView = CollectionViewSource.GetDefaultView(LayerRows);
            LayerRowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LayerRow.EntityType)));
            LayerRowsView.Filter = FilterLayerRow;

            var activeView = _uiApp.ActiveUIDocument.ActiveView;
            ContextBannerText =
                $"Context: Drafting View \"{activeView.Name}\"\n" +
                "Detail Lines and Filled Regions will be created in the active view.";

            LoadCadImports();
        }

        private void RefreshCatalog()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            AvailableLineStyles = new List<string> { LayerRow.MatchLayerName };
            AvailableLineStyles.AddRange(ProjectCatalogService.GetLineStyleNames(doc));
            AvailableFillPatterns = new List<string> { LayerRow.MatchLayerName };
            AvailableFillPatterns.AddRange(ProjectCatalogService.GetFilledRegionTypeNames(doc));

            DefaultLineStyle = LayerRow.MatchLayerName;
            DefaultFillPattern = LayerRow.MatchLayerName;
        }

        /// <summary>Restores the remembered mode and global picks (ignoring names no longer in the project).</summary>
        private void LoadSavedMapping()
        {
            _saved = MappingSettingsService.Load();

            if (AvailableLineStyles.Contains(_saved.GlobalLineStyle))
                DefaultLineStyle = _saved.GlobalLineStyle;
            if (AvailableFillPatterns.Contains(_saved.GlobalFillType))
                DefaultFillPattern = _saved.GlobalFillType;

            MappingMode = _saved.Mode;
        }

        /// <summary>Saves the mode, global picks and per-layer picks for the next session.</summary>
        public void SaveSettings()
        {
            _saved.Mode = MappingMode;
            _saved.GlobalLineStyle = DefaultLineStyle ?? LayerRow.MatchLayerName;
            _saved.GlobalFillType = DefaultFillPattern ?? LayerRow.MatchLayerName;

            foreach (var row in LayerRows)
            {
                var dict = row.EntityType == CadEntityType.Line ? _saved.LineOverrides : _saved.HatchOverrides;
                if (row.Override != null)
                    dict[row.LayerName] = row.Override;
                else
                    dict.Remove(row.LayerName);
            }

            string error = MappingSettingsService.Save(_saved);
            if (error != null)
                AddLog(LogLevel.Warning, $"Could not save mapping settings: {error}");
        }

        /// <summary>Re-points every row at the global line style / hatch, discarding per-layer picks.</summary>
        private void ApplyGlobalToAll()
        {
            foreach (var row in LayerRows)
                row.Override = null;

            RefreshShownStyles();
        }

        /// <summary>Updates each row's displayed value from the mode, the globals and any per-layer pick.</summary>
        private void RefreshShownStyles()
        {
            foreach (var row in LayerRows)
            {
                string global = row.EntityType == CadEntityType.Line ? DefaultLineStyle : DefaultFillPattern;
                bool useOverride = MappingMode == MappingMode.Multiple
                                   && row.Override != null
                                   && row.Options.Contains(row.Override);

                row.ShowWithoutOverride(useOverride ? row.Override : global ?? LayerRow.MatchLayerName);
            }
        }

        partial void OnMappingModeChanged(MappingMode value)
        {
            OnPropertyChanged(nameof(IsMultipleMode));
            RefreshShownStyles();
        }

        private bool CanConvert() =>
            SelectedCad != null && !IsRunning && LayerRows.Any(r => r.IsSelected);

        private bool FilterLayerRow(object obj)
        {
            if (string.IsNullOrWhiteSpace(LayerFilterText))
                return true;

            return obj is LayerRow row &&
                   row.LayerName.Contains(LayerFilterText, System.StringComparison.OrdinalIgnoreCase);
        }

        partial void OnLayerFilterTextChanged(string value) => LayerRowsView.Refresh();

        private void LoadCadImports()
        {
            var items = new CadImportCollectorService(_uiApp).GetAllCadImports();
            foreach (var item in items)
                AvailableCads.Add(item);

            // Default selected item = first CAD import in the list, per spec.
            if (AvailableCads.Count > 0)
                SelectedCad = AvailableCads[0];
        }

        partial void OnSelectedCadChanged(CadImportItem value)
        {
            ConvertCommand.NotifyCanExecuteChanged();
            RefreshLayers();
        }

        private void RefreshLayers()
        {
            foreach (var row in LayerRows)
                row.PropertyChanged -= OnLayerRowPropertyChanged;

            LayerRows.Clear();

            if (SelectedCad == null)
            {
                Metrics = ConversionMetrics.Empty;
                ConvertCommand.NotifyCanExecuteChanged();
                return;
            }

            var activeView = _uiApp.ActiveUIDocument.ActiveView;
            var doc = _uiApp.ActiveUIDocument.Document;

            var (entityCount, layerCount) = CadGeometryExtractor.PreScan(
                SelectedCad.ImportInstance, doc, activeView);

            Metrics = new ConversionMetrics
            {
                LayersFound = layerCount,
                Entities = entityCount,
                Placed = null,
                Skipped = null,
                Failed = null
            };

            var rows = CadGeometryExtractor.ScanLayers(SelectedCad.ImportInstance, doc, activeView);

            foreach (var row in rows)
            {
                bool isLine = row.EntityType == CadEntityType.Line;
                row.Options = isLine ? AvailableLineStyles : AvailableFillPatterns;
                var savedPicks = isLine ? _saved.LineOverrides : _saved.HatchOverrides;
                if (savedPicks.TryGetValue(row.LayerName, out string pick) && row.Options.Contains(pick))
                    row.Override = pick;
                row.PropertyChanged += OnLayerRowPropertyChanged;
                LayerRows.Add(row);
            }

            RefreshShownStyles();

            // Default selected item = first row (current screen's first object), per spec.
            SelectedLayerRow = LayerRows.FirstOrDefault();

            ConvertCommand.NotifyCanExecuteChanged();
        }

        private void OnLayerRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LayerRow.IsSelected))
                ConvertCommand.NotifyCanExecuteChanged();
        }

        private void SetAllVisible(bool value)
        {
            // Scoped to visible/filtered rows only, per DataGrid spec.
            foreach (var row in LayerRowsView.Cast<LayerRow>())
                row.IsSelected = value;

            ConvertCommand.NotifyCanExecuteChanged();
        }

        partial void OnDefaultLineStyleChanged(string value) => RefreshShownStyles();

        partial void OnDefaultFillPatternChanged(string value) => RefreshShownStyles();

        private void AddLog(LogLevel level, string message)
            => LogEntries.Add(new LogEntry(level, message));

        private void CopyAllLog()
        {
            if (LogEntries.Count == 0)
                return;

            try
            {
                System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, LogEntries.Select(e => e.ToString())));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Clipboard can transiently fail if another process holds it
                // (common on Windows). Not worth surfacing as a hard error â€”
                // log it so the attempt isn't silently swallowed.
                AddLog(LogLevel.Warning, "Copy to clipboard failed (clipboard busy) â€” try again.");
            }
        }

        private void CopySelectedLog()
        {
            if (SelectedLogEntries == null || SelectedLogEntries.Count == 0)
            {
                AddLog(LogLevel.Warning, "No log rows selected.");
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(string.Join(
                    Environment.NewLine,
                    SelectedLogEntries.Cast<LogEntry>().Select(e => e.ToString())));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                AddLog(LogLevel.Warning, "Copy to clipboard failed (clipboard busy) â€” try again.");
            }
        }

        private void ExportLog()
        {
            if (LogEntries.Count == 0)
            {
                AddLog(LogLevel.Warning, "Nothing to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = $"DwgToDetailLines_Logs_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt",
                Filter = "Text file (*.txt)|*.txt"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllLines(dialog.FileName, LogEntries.Select(e => e.ToString()));
                AddLog(LogLevel.Info, $"Log exported to '{dialog.FileName}'");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Export failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the status-strip progress bar. Conversion runs on the UI thread inside the
        /// Revit API event, so the dispatcher is pumped at render priority to repaint the bar.
        /// </summary>
        private void ReportProgress(double percent, string text)
        {
            Progress = percent;
            SummaryText = text;

            System.Windows.Application.Current?.Dispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
        }

        private void Convert()
        {
            IsRunning = true;
            Progress = 0;
            SummaryText = "Starting...";
            ConvertCommand.NotifyCanExecuteChanged();

            var cad = SelectedCad.ImportInstance;
            var spline = SplineHandlingMode;
            var transform = TransformMethod;
            int entityCount = Metrics.Entities;
            int layerCount = Metrics.LayersFound;

            var selectedLineLayers = LayerRows
                .Where(r => r.IsSelected && r.EntityType == CadEntityType.Line)
                .Select(r => r.LayerName)
                .ToList();

            var selectedHatchLayers = LayerRows
                .Where(r => r.IsSelected && r.EntityType == CadEntityType.Hatch)
                .Select(r => r.LayerName)
                .ToList();

            // The style / type each selected layer will use: ShownStyle already reflects the
            // mode (global in Single, own pick or global in Multiple).
            var lineMap = LayerRows
                .Where(r => r.IsSelected && r.EntityType == CadEntityType.Line)
                .ToDictionary(r => r.LayerName, r => r.ShownStyle);
            var hatchMap = LayerRows
                .Where(r => r.IsSelected && r.EntityType == CadEntityType.Hatch)
                .ToDictionary(r => r.LayerName, r => r.ShownStyle);

            string lineStyle = DefaultLineStyle;
            // Base type the service duplicates when it has to create a missing fill type.
            string fillPattern = DefaultFillPattern == LayerRow.MatchLayerName
                ? AvailableFillPatterns.FirstOrDefault(n => n != LayerRow.MatchLayerName)
                : DefaultFillPattern;

            SaveSettings();

            _convertHandler.Raise(_convertEvent, uiApp =>
            {
                // Esc / Cancel with a Yes-No confirm; the conversion transaction is rolled back on cancel.
                using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.DwgToDlp, uiApp);
                try
                {
                    var service = new DetailLineConversionService(uiApp, e => LogEntries.Add(e), ReportProgress);

                    var updatedMetrics = service.Execute(
                        cad, spline, transform, entityCount, layerCount,
                        selectedLineLayers, selectedHatchLayers,
                        lineStyle, fillPattern, lineMap, hatchMap);

                    Metrics = updatedMetrics;
                    Progress = 100;
                    SummaryText = $"Done: {updatedMetrics.Placed} placed, {updatedMetrics.Skipped} skipped, {updatedMetrics.Failed} failed";
                }
                catch (OperationCanceledException)
                {
                    Progress = 0;
                    SummaryText = "Cancelled by user. Nothing was placed.";
                    AddLog(LogLevel.Warning, SummaryText);
                }
                catch (Exception ex)
                {
                    AddLog(LogLevel.Error, ex.Message);
                    TaskDialog.Show("DWG to Detail Lines", $"Conversion failed:\n{ex.Message}");
                }
                finally
                {
                    IsRunning = false;
                    ConvertCommand.NotifyCanExecuteChanged();
                }
            });
        }
    }
}

