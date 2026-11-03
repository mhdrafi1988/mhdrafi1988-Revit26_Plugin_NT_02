using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.UI.ViewModels
{
    public partial class FloorComparisonViewModel : ObservableObject
    {
        public UIDocument UIDoc { get; }
        public UIApplication App { get; }
        public ElementId FloorAId { get; }
        public ElementId FloorBId { get; }

        // â”€â”€ Tolerances â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private double positionToleranceMm = 10;

        [ObservableProperty]
        private double elevationToleranceMm = 1;

        // â”€â”€ Marker styles â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public MarkerStyleGroup MissingMarkerGroup { get; } = new MarkerStyleGroup
        {
            GroupLabel = "Missing Point",
            ColorName = "Red",
            RadiusMm = 150
        };

        public MarkerStyleGroup MismatchMarkerGroup { get; } = new MarkerStyleGroup
        {
            GroupLabel = "Mismatched Elevation",
            ColorName = "Orange",
            RadiusMm = 150
        };

        public IReadOnlyList<string> ColorPalette { get; } = NamedColorHelper.PaletteNames;

        // â”€â”€ Metrics (bound by the Metrics Card) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private ComparisonMetrics metrics = new ComparisonMetrics();

        // â”€â”€ Log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();

        [ObservableProperty]
        private string statusMessage = "Ready";

        [ObservableProperty]
        private bool isBusy;

        // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty]
        private double progress;

        private ComparisonResult _lastResult;

        public FloorComparisonViewModel(UIDocument uidoc, UIApplication app, ElementId floorAId, ElementId floorBId)
        {
            UIDoc = uidoc;
            App = app;
            FloorAId = floorAId;
            FloorBId = floorBId;

            FloorComparisonEventManager.Init();

            Recalculate();
        }

        // â”€â”€ Recalculate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand]
        private void Recalculate()
        {
            IsBusy = true;
            StatusMessage = "Comparing...";
            AddLog(new LogEntry(LogLevel.Info,
                $"Comparing shape-editing points (position tolerance {PositionToleranceMm} mm, elevation tolerance {ElevationToleranceMm} mm)..."));

            FloorComparisonHandler.Payload = new ComparisonPayload
            {
                Mode = ComparisonMode.Recalculate,
                FloorAId = FloorAId,
                FloorBId = FloorBId,
                PositionToleranceMm = PositionToleranceMm,
                ElevationToleranceMm = ElevationToleranceMm,
                MissingMarkerGroup = MissingMarkerGroup,
                MismatchMarkerGroup = MismatchMarkerGroup,
                Log = AddLog,
                OnRecalculated = OnRecalculated
            };

            FloorComparisonEventManager.Event.Raise();
        }

        private void OnRecalculated(ComparisonResult result)
        {
            RunOnUiThread(() =>
            {
                IsBusy = false;

                if (!result.Success)
                {
                    StatusMessage = "Failed";
                    AddLog(new LogEntry(LogLevel.Error, $"Comparison failed: {result.ErrorMessage}"));
                    return;
                }

                _lastResult = result;
                Metrics = result.Metrics;
                StatusMessage = "Done";

                AddLog(new LogEntry(LogLevel.Success,
                    $"Compared {result.Metrics.TotalPointsA} vs {result.Metrics.TotalPointsB} points â€” " +
                    $"{result.Metrics.MatchedCount} matched, {result.Metrics.DifferencesCount} difference(s)."));

                PlaceMarkersCommand.NotifyCanExecuteChanged();
            });
        }

        // â”€â”€ PlaceMarkers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand(CanExecute = nameof(CanPlaceMarkers))]
        private void PlaceMarkers()
        {
            IsBusy = true;
            StatusMessage = "Placing markers...";
            AddLog(new LogEntry(LogLevel.Info, "Placing comparison markers in the active view..."));

            FloorComparisonHandler.Payload = new ComparisonPayload
            {
                Mode = ComparisonMode.PlaceMarkers,
                FloorAId = FloorAId,
                FloorBId = FloorBId,
                PositionToleranceMm = PositionToleranceMm,
                ElevationToleranceMm = ElevationToleranceMm,
                MissingMarkerGroup = MissingMarkerGroup,
                MismatchMarkerGroup = MismatchMarkerGroup,
                Log = AddLog,
                OnRecalculated = OnRecalculated,
                OnMarkersPlaced = OnMarkersPlaced
            };

            FloorComparisonEventManager.Event.Raise();
        }

        private bool CanPlaceMarkers() => _lastResult != null && _lastResult.Success && !IsBusy;

        private void OnMarkersPlaced(int missingPlaced, int mismatchPlaced)
        {
            RunOnUiThread(() =>
            {
                AddLog(new LogEntry(LogLevel.Success,
                    $"Placed {missingPlaced} missing-point circle(s) and {mismatchPlaced} mismatched-elevation circle(s)."));
            });
        }

        // â”€â”€ ClearLog â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand]
        private void ClearLog()
        {
            LogEntries.Clear();
            AddLog(new LogEntry(LogLevel.Info, "Log cleared."));
        }

        private void AddLog(LogEntry entry) => RunOnUiThread(() => LogEntries.Add(entry));

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }
    }
}

