// ==================================
// File: CreaserAdvViewModel.cs
// Namespace: Revit26_Plugin_Revised.CreaserAdv.V012.ViewModels
// ==================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.CreaserAdv.V012.Core.Models;
using Revit26_Plugin_Revised.CreaserAdv.V012.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.CreaserAdv.V012.Services;
using Revit26_Plugin_Revised.Shared.Models;          // LogEntry, LogLevel, converters
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.CreaserAdv.V012.ViewModels
{
    /// <summary>
    /// ViewModel for Creaser Advanced V010.
    ///
    /// Pipeline (runs in <see cref="Core.Engine.CreaserAdvEngine"/> via an ExternalEvent):
    ///   1. Extract crease curves  (top-face / top-face solid edges)
    ///   1b. Filter out horizontal creases (same Z on both endpoints) â€” always runs
    ///   2. Optionally extract boundary curves  (top-face / side-face edges)
    ///   2b. (Optional, ticked by default) Filter by Dijkstra path validity â€”
    ///       creases + boundary together; each point keeps exactly one edge,
    ///       its single shortest descending route to a minimum-elevation
    ///       drain node (per connected group of edges). An optional
    ///       minimum-slope threshold (also toggled here) rejects edges before
    ///       path-finding. Ridge points (no valid descent) are excluded and
    ///       counted in the run summary.
    ///   3. Project all curves to plan-view Z elevation (curve+line pairs kept
    ///      index-aligned â€” a dropped zero-length projection removes both)
    ///   4. (Optional) Filter creases by minimum length (curve+line pairs, index-aligned)
    ///   5. (Optional) Group creases by drain proximity, remove longest per start point
    ///   6. Place detail items along filtered lines
    ///   7. Populate <see cref="Summary"/> for the summary bar
    /// </summary>
    public partial class CreaserAdvViewModel : ObservableObject
    {
        // --------------------------------------------------
        // Fields
        // --------------------------------------------------

        private readonly Document       _doc;
        private readonly ElementId      _roofId;
        private readonly LoggingService _log;

        /// <summary>The window's (Revit's main) thread; run results are marshalled back to it.</summary>
        private readonly Dispatcher     _dispatcher;

        // --------------------------------------------------
        // UI-bound collections
        // --------------------------------------------------

        public ObservableCollection<FamilySymbol> DetailSymbols { get; }
            = new ObservableCollection<FamilySymbol>();

        /// <summary>Every log entry, unfiltered. Entries come from LoggingService.</summary>
        public ObservableCollection<LogEntry> LogEntries => _log.Entries;

        /// <summary>
        /// The standalone window's log list. Same entries as <see cref="LogEntries"/>,
        /// with verbose (<see cref="LogLevel.Debug"/>) lines hidden when
        /// <see cref="ShowDetailLog"/> is off. A private view, so filtering here does
        /// not affect other bindings to <see cref="LogEntries"/> (e.g. the Combined tab).
        /// </summary>
        public ICollectionView VisibleLogEntries { get; }

        // --------------------------------------------------
        // Observable properties
        // --------------------------------------------------

        [ObservableProperty]
        private FamilySymbol _selectedDetailSymbol;

        /// <summary>
        /// Drives the "Include boundary lines" checkbox.
        /// Persists within the session; false when the window first opens.
        /// </summary>
        [ObservableProperty]
        private bool _includeBoundaryLines = false;

        /// <summary>
        /// Drives the "Group by drain points" checkbox.
        /// When enabled, creases are grouped by proximity and longest per start point is removed.
        /// </summary>
        [ObservableProperty]
        private bool _enableDrainGrouping = true;

        /// <summary>
        /// Drives the "Filter by Dijkstra path validity" checkbox (ticked by default).
        /// When enabled, every crease/boundary segment must genuinely descend toward
        /// a drain node (the lowest node of its connected group of edges). Each
        /// non-drain point keeps exactly one outgoing edge â€” its single shortest
        /// descending route. Ridge points (no valid descent) are excluded and
        /// counted in the run summary.
        /// Runs on creases + boundary curves together, right after the horizontal filter,
        /// before minimum-length and drain-proximity grouping.
        /// </summary>
        [ObservableProperty]
        private bool _enableDijkstraPathFilter = true;

        /// <summary>
        /// Drives the "Enforce minimum slope" checkbox in the Minimum Slope card.
        /// Only meaningful (and only shown enabled in the UI) when
        /// <see cref="EnableDijkstraPathFilter"/> is also on â€” the slope check
        /// runs inside the same Dijkstra graph-build step.
        /// </summary>
        [ObservableProperty]
        private bool _enableMinimumSlope = true;

        /// <summary>
        /// Minimum required slope as a percentage (Î”Z / horizontal 2D length Ã— 100).
        /// Edges below this are rejected before path-finding. Default 1.5%.
        /// </summary>
        [ObservableProperty]
        private double _minimumSlopePercent = 1.5;

        /// <summary>
        /// Proximity radius for drain point grouping in millimeters (default 500mm).
        /// </summary>
        [ObservableProperty]
        private double _drainGroupingRadiusMm = 500.0;

        /// <summary>
        /// Drives the "Drop lines less than minimum" checkbox.
        /// When enabled, crease lines shorter than threshold are removed (applies to creases only).
        /// </summary>
        [ObservableProperty]
        private bool _enableMinimumLength = true;

        /// <summary>
        /// Minimum line length threshold in millimeters (default 500mm).
        /// Only applies to crease lines, not boundary lines.
        /// </summary>
        [ObservableProperty]
        private double _minimumLengthMm = 500.0;

        /// <summary>Populated after every Run. Null before the first run.</summary>
        [ObservableProperty]
        private RunSummary _summary;

        /// <summary>Controls summary bar visibility â€” false until first Run.</summary>
        [ObservableProperty]
        private bool _hasSummary = false;

        /// <summary>True from the moment Run is requested until the engine reports back; blocks a second overlapping Run.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool _isRunning = false;

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ──
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _summaryText = string.Empty;

        /// <summary>Drives the "Show detail" checkbox under the log. On by default.</summary>
        [ObservableProperty]
        private bool _showDetailLog = true;

        partial void OnShowDetailLogChanged(bool value) => VisibleLogEntries.Refresh();

        // --------------------------------------------------
        // Constructor
        // --------------------------------------------------

        /// <summary>
        /// Builds the ViewModel for <paramref name="roof"/> in the active project. Must be created on
        /// Revit's main thread (it captures that thread's dispatcher for run results).
        /// </summary>
        public CreaserAdvViewModel(
            UIApplication  uiApp,
            Element        roof,
            LoggingService log)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));

            _doc    = uiApp.ActiveUIDocument.Document;
            _roofId = (roof ?? throw new ArgumentNullException(nameof(roof))).Id;
            _log    = log  ?? throw new ArgumentNullException(nameof(log));

            // Captured here, on the thread that creates the window. Application.Current
            // is not guaranteed to exist inside Revit.
            _dispatcher = Dispatcher.CurrentDispatcher;

            VisibleLogEntries = new ListCollectionView(_log.Entries)
            {
                Filter = o => ShowDetailLog || (o is LogEntry e && e.Level != LogLevel.Debug)
            };

            LoadDetailSymbols();

            CreaserAdvEventManager.Init();
        }

        // --------------------------------------------------
        // Load detail symbols
        // --------------------------------------------------

        private void LoadDetailSymbols()
        {
            DetailSymbols.Clear();

            foreach (FamilySymbol s in new DetailItemCollectorService(_doc, _log).Collect())
                DetailSymbols.Add(s);

            if (DetailSymbols.Count > 0)
                SelectedDetailSymbol = DetailSymbols[0];
        }

        // --------------------------------------------------
        // Run command (via ExternalEvent)
        // --------------------------------------------------
        // A modeless window cannot call the Revit API directly from a button
        // click â€” the pipeline that used to run here directly now lives in
        // CreaserAdvEngine, invoked through CreaserAdvHandler/EventManager,
        // matching the convention already used by InnerLoopDivider/
        // InnerLoopsAndPerpendicular/OuterCurveDivider/AutoSlopeByDrain.

        /// <summary>Raised after Run completes or bails out early (true = success),
        /// so callers outside this ViewModel (e.g. the Combined Roof Tools
        /// "Run All" orchestrator) can await completion without polling.</summary>
        public event Action<bool> RunCompleted;

        private bool CanRun() => !IsRunning;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            if (SelectedDetailSymbol == null)
            {
                _log.Warning("Please select a detail item. " +
                             (DetailSymbols.Count == 0
                                 ? "None are available â€” this project has no line-based detail component family loaded."
                                 : string.Empty));
                RunCompleted?.Invoke(false);
                return;
            }

            if (!TryValidateInputs(out string problem))
            {
                _log.Error($"Cannot run: {problem}");
                RunCompleted?.Invoke(false);
                return;
            }

            IsRunning = true;
            _log.Info("Run requested â€” waiting for Revit to pick up the requestâ€¦");

            CreaserAdvHandler.Payload = new CreaserAdvPayload
            {
                Document = _doc,
                RoofId = _roofId,
                SelectedDetailSymbolId = SelectedDetailSymbol.Id,
                IncludeBoundaryLines = IncludeBoundaryLines,
                EnableDrainGrouping = EnableDrainGrouping,
                EnableDijkstraPathFilter = EnableDijkstraPathFilter,
                EnableMinimumSlope = EnableMinimumSlope,
                MinimumSlopePercent = MinimumSlopePercent,
                DrainGroupingRadiusMm = DrainGroupingRadiusMm,
                EnableMinimumLength = EnableMinimumLength,
                MinimumLengthMm = MinimumLengthMm,
                Log = _log,
                OnCompleted = result =>
                {
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsRunning = false;

                        if (!result.Success)
                        {
                            _log.Error($"Run failed: {result.ErrorMessage}");
                            RunCompleted?.Invoke(false);
                            return;
                        }

                        UpdateSummary(result.CreasesFound, result.BoundaryFound, result.Created, result.Failed,
                            result.RidgePoints, result.DisconnectedPoints);
                        RunCompleted?.Invoke(true);
                    }));
                }
            };

            ExternalEventRequest request = CreaserAdvEventManager.Event.Raise();
            _log.Debug($"  ExternalEvent request status: {request}.");

            if (request == ExternalEventRequest.Denied || request == ExternalEventRequest.TimedOut)
            {
                // Revit refused the request, so the handler will never call back.
                CreaserAdvHandler.Payload = null;
                IsRunning = false;
                _log.Error($"Revit did not accept the run request ({request}). Try again in a moment.");
                RunCompleted?.Invoke(false);
            }
        }

        // --------------------------------------------------
        // Clear / copy / open log
        // --------------------------------------------------

        [RelayCommand]
        private void ClearLog() => _log.Clear();

        [RelayCommand]
        private void CopyLog()
        {
            if (_log.Entries.Count == 0) return;

            var sb = new StringBuilder();
            foreach (LogEntry entry in _log.Entries)
                sb.AppendLine(entry.ToString());

            System.Windows.Clipboard.SetText(sb.ToString());
            _log.Info("Log copied to System.Windows.Clipboard.");
        }

        [RelayCommand]
        private void OpenLogFile()
        {
            try
            {
                Process.Start(new ProcessStartInfo(_log.LogFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Warning($"Could not open the log file ({_log.LogFilePath}): {ex.Message}");
            }
        }

        // --------------------------------------------------
        // Private helpers
        // --------------------------------------------------

        private bool TryValidateInputs(out string problem)
        {
            problem = null;

            if (EnableMinimumLength && (double.IsNaN(MinimumLengthMm) || MinimumLengthMm < 0))
                problem = "Minimum length must be zero or more.";
            else if (EnableDrainGrouping && (double.IsNaN(DrainGroupingRadiusMm) || DrainGroupingRadiusMm < 0))
                problem = "Drain grouping radius must be zero or more.";
            else if (EnableDijkstraPathFilter && EnableMinimumSlope && (double.IsNaN(MinimumSlopePercent) || MinimumSlopePercent < 0))
                problem = "Minimum slope must be zero or more.";

            return problem == null;
        }

        private void UpdateSummary(int creasesFound, int boundaryFound, int created, int failed, int ridgePoints = 0, int disconnectedPoints = 0)
        {
            Summary    = new RunSummary(creasesFound, boundaryFound, created, failed, ridgePoints, disconnectedPoints);
            HasSummary = true;
        }
    }
}

