using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Events;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.ViewModels
{
    /// <summary>Main ViewModel for the EPS Stepped Insulation Layout window.</summary>
    public sealed partial class ViewModel_EpsSteppedInsulation : ObservableObject
    {
        private readonly RunInsulationHandler _runHandler;
        private readonly ExternalEvent _runEvent;
        private readonly RefreshRoofHandler _refreshHandler;
        private readonly ExternalEvent _refreshEvent;
        private bool _detached;

        public ViewModel_EpsSteppedInsulation(
            InsulationRoofContext context,
            IReadOnlyList<RoofTypeOption> roofTypes,
            RunInsulationHandler runHandler,
            ExternalEvent runEvent,
            RefreshRoofHandler refreshHandler,
            ExternalEvent refreshEvent)
        {
            Context = context;
            AllRoofTypes = roofTypes;
            _runHandler = runHandler;
            _runEvent = runEvent;
            _refreshHandler = refreshHandler;
            _refreshEvent = refreshEvent;

            _runHandler.Completed += OnPlacementCompleted;
            _refreshHandler.Completed += OnRoofRefreshed;

            ToolTitle = ToolCatalog.EpsSteppedInsulationLayout.Title;
            RoofName = context.RoofName;
            LevelName = context.LevelName;
            DrainCount = context.Drains?.Count ?? 0;

            // Build edge list for alignment picker
            if (context.OutlineMetres != null)
            {
                var edges = SteppedLayoutEngine.GetEdges(context.OutlineMetres);
                int longestIdx = SteppedLayoutEngine.LongestEdgeIndex(context.OutlineMetres);
                EdgeOptions = new ObservableCollection<EdgeOption>(
                    edges.Select(e => new EdgeOption(e.Index, e.LengthM, e.Index == longestIdx)));
                _selectedEdgeIndex = longestIdx;
            }
            else
            {
                EdgeOptions = new ObservableCollection<EdgeOption>();
            }

            // Pre-select roof types if obvious matches exist
            _selectedBaseType = AllRoofTypes.FirstOrDefault(t => Math.Abs(t.ThicknessMm - 100) < 1);
            _selectedStepType = AllRoofTypes.FirstOrDefault(t => Math.Abs(t.ThicknessMm - 35) < 1);

            Recalculate();
        }

        // --- Roof info ---

        public string ToolTitle { get; }

        [ObservableProperty]
        private string _roofName;

        [ObservableProperty]
        private string _levelName;

        [ObservableProperty]
        private int _drainCount;

        public InsulationRoofContext Context { get; private set; }

        // --- Slope input ---

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool _useSlopeRatio = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private string _slopeRatioText = "60";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private string _slopePercentText = "1.667";

        partial void OnUseSlopeRatioChanged(bool value) => Recalculate();
        partial void OnSlopeRatioTextChanged(string value) => Recalculate();
        partial void OnSlopePercentTextChanged(string value) => Recalculate();

        // --- Edge alignment ---

        public ObservableCollection<EdgeOption> EdgeOptions { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private int _selectedEdgeIndex;

        partial void OnSelectedEdgeIndexChanged(int value) => Recalculate();

        // --- Roof types ---

        public IReadOnlyList<RoofTypeOption> AllRoofTypes { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private RoofTypeOption _selectedBaseType;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private RoofTypeOption _selectedStepType;

        // --- Summary ---

        [ObservableProperty] private int _totalCells;
        [ObservableProperty] private int _fullCells;
        [ObservableProperty] private int _trimmedCells;
        [ObservableProperty] private int _maxLayers;
        [ObservableProperty] private int _totalPanels;
        [ObservableProperty] private string _totalAreaText = "—";
        [ObservableProperty] private string _totalVolumeText = "—";
        [ObservableProperty] private string _layerBreakdown = "";

        // --- Validation ---

        [ObservableProperty] private string _validationMessage = "";
        [ObservableProperty] private bool _hasValidationError;

        // --- Status ---

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _statusText = "Ready";

        // --- Layout result ---

        private LayoutResult _currentLayout;

        // --- Commands ---

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            if (_currentLayout == null || !_currentLayout.IsValid) return;
            if (SelectedBaseType == null || SelectedStepType == null) return;

            IsBusy = true;
            StatusText = "Placing insulation panels...";

            _runHandler.Pending = new PlacementRequest
            {
                Context = Context,
                Layout = _currentLayout,
                BaseRoofTypeId = SelectedBaseType.Id,
                StepRoofTypeId = SelectedStepType.Id,
                Settings = BuildSettings()
            };
            _runEvent.Raise();
        }

        private bool CanRun() =>
            !IsBusy &&
            _currentLayout != null &&
            _currentLayout.IsValid &&
            SelectedBaseType != null &&
            SelectedStepType != null &&
            ParseSlope(out _);

        [RelayCommand]
        private void Refresh()
        {
            _refreshHandler.Current = Context;
            _refreshEvent.Raise();
        }

        // --- Recalculate ---

        private void Recalculate()
        {
            if (Context?.OutlineMetres == null) return;

            if (!ParseSlope(out double slopePercent))
            {
                HasValidationError = true;
                ValidationMessage = "Enter a valid slope value.";
                _currentLayout = null;
                RunCommand.NotifyCanExecuteChanged();
                return;
            }

            // Validate types
            string typeErr = ValidateTypes();
            if (typeErr != null)
            {
                HasValidationError = true;
                ValidationMessage = typeErr;
            }
            else
            {
                HasValidationError = false;
                ValidationMessage = "";
            }

            var settings = BuildSettings();
            settings.SlopePercent = slopePercent;

            _currentLayout = SteppedLayoutEngine.Compute(
                Context.OutlineMetres, Context.Drains, settings);

            if (!_currentLayout.IsValid)
            {
                HasValidationError = true;
                ValidationMessage = string.Join("\n", _currentLayout.Errors);
                RunCommand.NotifyCanExecuteChanged();
                return;
            }

            TotalCells = _currentLayout.TotalCells;
            FullCells = _currentLayout.FullCells;
            TrimmedCells = _currentLayout.TrimmedCells;
            MaxLayers = _currentLayout.MaxStepLayers;
            TotalPanels = _currentLayout.TotalPanels;
            TotalAreaText = $"{_currentLayout.TotalAreaM2:N2} m²";
            TotalVolumeText = $"{_currentLayout.TotalVolumeM3:N3} m³";

            // Layer breakdown
            var lines = new List<string>();
            for (int i = 0; i < _currentLayout.PanelsPerLayer.Count; i++)
            {
                string label = i == 0 ? "Base (100 mm)" : $"Step {i} (35 mm)";
                lines.Add($"{label}: {_currentLayout.PanelsPerLayer[i]} panels");
            }
            LayerBreakdown = string.Join("\n", lines);

            RunCommand.NotifyCanExecuteChanged();
        }

        private bool ParseSlope(out double slopePercent)
        {
            slopePercent = 0;
            if (UseSlopeRatio)
            {
                if (!double.TryParse(SlopeRatioText, out double ratio) || ratio <= 0)
                    return false;
                slopePercent = 100.0 / ratio;
            }
            else
            {
                if (!double.TryParse(SlopePercentText, out double pct) || pct <= 0)
                    return false;
                slopePercent = pct;
            }
            return true;
        }

        private string ValidateTypes()
        {
            if (SelectedBaseType != null && Math.Abs(SelectedBaseType.ThicknessMm - 100) > 1)
                return $"Base type thickness is {SelectedBaseType.ThicknessMm:0} mm — expected 100 mm.";
            if (SelectedStepType != null && Math.Abs(SelectedStepType.ThicknessMm - 35) > 1)
                return $"Step type thickness is {SelectedStepType.ThicknessMm:0} mm — expected 35 mm.";
            return null;
        }

        private InsulationSettings BuildSettings()
        {
            ParseSlope(out double slope);
            return new InsulationSettings
            {
                SlopePercent = slope,
                AlignEdgeIndex = SelectedEdgeIndex
            };
        }

        // --- Event callbacks ---

        private void OnPlacementCompleted(PlacementResult result)
        {
            IsBusy = false;
            StatusText = result.Success
                ? $"Done — {result.RoofsCreated} panels created."
                : $"Failed — {result.Message}";
        }

        private void OnRoofRefreshed(InsulationRoofContext ctx)
        {
            Context = ctx;
            RoofName = ctx.RoofName;
            LevelName = ctx.LevelName;
            DrainCount = ctx.Drains?.Count ?? 0;
            Recalculate();
        }

        /// <summary>Call from window Closed. Unhooks events safely from the WPF thread.</summary>
        public void Detach()
        {
            if (_detached) return;
            _detached = true;
            _runHandler.Completed -= OnPlacementCompleted;
            _refreshHandler.Completed -= OnRoofRefreshed;
        }
    }

    /// <summary>One edge option for the alignment combo box.</summary>
    public sealed class EdgeOption
    {
        public EdgeOption(int index, double lengthM, bool isLongest)
        {
            Index = index;
            LengthM = lengthM;
            IsLongest = isLongest;
        }

        public int Index { get; }
        public double LengthM { get; }
        public bool IsLongest { get; }

        public string DisplayName =>
            IsLongest ? $"Edge {Index + 1} — {LengthM:N2} m (longest)" : $"Edge {Index + 1} — {LengthM:N2} m";
    }
}
