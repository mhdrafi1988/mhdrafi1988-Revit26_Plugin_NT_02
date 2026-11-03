// =======================================================
// File: CurveDividerViewModel.cs
// Location: UI/ViewModels/
// Changes vs V002:
//   Converted from a hand-wired IRelayCommand-in-constructor pattern to
//   [RelayCommand] source-generator attributes, per this suite's stack
//   convention (was already ObservableObject-based, just not using the
//   attributes).
//   Constructor now takes (ElementId floorId, initial edges, filtered-line
//   count) instead of (Document doc, Floor floor) — a modeless window
//   cannot hold a live Document/Floor and call Revit API methods on it
//   directly from a button click. Apply() now raises the ExternalEvent
//   instead of calling CurveDivisionService directly; the floor is
//   re-resolved from FloorId inside CurveDividerEngine, which only ever
//   runs inside a valid API context (Command or ExternalEvent handler).
//   The first edge-extraction pass is done by the Command before the
//   window opens and passed in via initialEdges — there is no
//   "re-extract" button in this tool, so that's the only Analyze-style
//   read needed.
// =======================================================

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Models;
using Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.UI.ViewModels
{
    public partial class CurveDividerViewModel : ObservableObject
    {
        private readonly ElementId _floorId;

        public ObservableCollection<EdgeTypeSetting> TypeSettings { get; } = new ObservableCollection<EdgeTypeSetting>();
        public ObservableCollection<CurveEdgeModel>  Edges        { get; } = new ObservableCollection<CurveEdgeModel>();

        /// <summary>Shared log rows (timestamp + level + message), colored by LogLevelToColorConverter.</summary>
        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();

        public string[] ModeOptions { get; } = { "Distance", "Count" };

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ──
        [ObservableProperty] private bool   isRunning;
        [ObservableProperty] private double progress;
        [ObservableProperty] private string summaryText = "";

        public CurveDividerViewModel(ElementId floorId, List<CurveEdgeModel> initialEdges, int filteredLines)
        {
            _floorId = floorId;

            PopulateEdges(initialEdges, filteredLines);

            CurveDividerEventManager.Init();
        }

        private void PopulateEdges(List<CurveEdgeModel> found, int filteredLines)
        {
            Edges.Clear();
            TypeSettings.Clear();

            var settingByType = new Dictionary<string, EdgeTypeSetting>();
            foreach (var typeName in found.Select(e => e.CurveTypeName).Distinct())
            {
                var setting = new EdgeTypeSetting { TypeName = typeName };
                setting.PropertyChanged += OnTypeSettingChanged;
                settingByType[typeName] = setting;
                TypeSettings.Add(setting);
            }

            foreach (var edge in found)
            {
                edge.TypeSetting = settingByType[edge.CurveTypeName];
                Edges.Add(edge);
            }

            Log(LogLevel.Info, $"Floor loaded — {Edges.Count} non-linear edge(s) across {TypeSettings.Count} type(s).");
            Log(LogLevel.Info, "Per-edge point counts seeded from edge length (4 / 8 / 12 / L÷2).");
            if (filteredLines > 0) Log(LogLevel.Info, $"{filteredLines} straight Line edge(s) filtered out.");
            if (Edges.Count == 0)  Log(LogLevel.Warning, "No curved edges on this floor's top face.");
        }

        private void OnTypeSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!(sender is EdgeTypeSetting setting)) return;

            foreach (var edge in Edges.Where(x => x.TypeSetting == setting && !x.HasOverride))
            {
                bool bulkCount = e.PropertyName == nameof(EdgeTypeSetting.FixedCount)
                                 && setting.Mode == DivisionMode.ByCount
                                 && !edge.IsManual;
                if (bulkCount) edge.PointCount = setting.FixedCount;

                edge.NotifyInheritedRuleChanged();
            }
        }

        [RelayCommand]
        private void SelectAll() => SetAllSelected(true);

        [RelayCommand]
        private void SelectNone() => SetAllSelected(false);

        private void SetAllSelected(bool selected)
        {
            foreach (var edge in Edges) edge.IsSelected = selected;
        }

        /// <summary>Raised after Apply's ExternalEvent round-trip completes (true =
        /// success), so callers outside this ViewModel (e.g. the Combined Floor
        /// Tools "Run All" orchestrator) can await completion without polling.</summary>
        public event Action<bool> ApplyCompleted;

        [RelayCommand]
        private void Apply()
        {
            var selected = Edges.Where(e => e.IsSelected).ToList();
            if (!selected.Any())
            {
                Log(LogLevel.Warning, "Apply skipped — no edges selected.");
                ApplyCompleted?.Invoke(false);
                return;
            }

            int manual    = selected.Count(e => e.IsCountDriven && e.IsManual);
            int overrides = selected.Count(e => e.HasOverride);
            int totalPts  = selected.Sum(e => e.FinalPointCount);
            Log(LogLevel.Info, $"Applying to {selected.Count} edge(s): {totalPts} point(s) ({overrides} override, {manual} manual)...");

            CurveDividerHandler.Payload = new CurveDividerPayload
            {
                FloorId = _floorId,
                SelectedEdges = selected,
                Log = AddLogEntry,
                OnCompleted = result =>
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!result.Success)
                        {
                            Log(LogLevel.Error, $"Apply failed: {result.ErrorMessage}");
                            ApplyCompleted?.Invoke(false);
                            return;
                        }

                        if (result.LogEntries != null)
                            foreach (var entry in result.LogEntries)
                                AddLogEntry(entry);

                        ApplyCompleted?.Invoke(true);
                    }));
                }
            };

            CurveDividerEventManager.Event.Raise();
        }

        private void Log(LogLevel level, string message) => AddLogEntry(new LogEntry(level, message));

        private void AddLogEntry(LogEntry entry)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
                LogEntries.Add(entry);
            else
                dispatcher.BeginInvoke(new Action(() => LogEntries.Add(entry)));
        }
    }
}
