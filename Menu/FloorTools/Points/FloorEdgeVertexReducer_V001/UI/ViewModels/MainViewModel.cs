using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Core.Models;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Core.Services;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private FloorEdgeVertexReducerEventHandler _handler;
        private Autodesk.Revit.UI.ExternalEvent _externalEvent;

        [ObservableProperty] private string floorLabel = "Floor: (none selected)";
        [ObservableProperty] private double toleranceMm = 5.0;

        /// <summary>
        /// Outer-loop only. Number of positions (loop-traversal order) away from a
        /// qualifying max-Z point to also keep, on each side. 0 disables the rule.
        /// </summary>
        [ObservableProperty] private int neighborOffset = 1;

        [ObservableProperty] private bool canPreview;
        [ObservableProperty] private bool canApply;
        [ObservableProperty] private string summaryText = "No preview run yet";

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ──
        [ObservableProperty] private bool   isRunning;
        [ObservableProperty] private double progress;

        public ObservableCollection<VertexPreviewRow> PreviewRows { get; } = new();
        public ObservableCollection<LogEntry> Logs { get; } = new();

        public void Initialize(FloorEdgeVertexReducerEventHandler handler, Autodesk.Revit.UI.ExternalEvent externalEvent)
        {
            _handler = handler;
            _externalEvent = externalEvent;
        }

        [RelayCommand]
        private void SelectFloor()
        {
            _handler.PendingRequest = ReducerRequest.SelectFloor;
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void Preview()
        {
            CanPreview = false;
            _handler.PendingRequest = ReducerRequest.Preview;
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void Apply()
        {
            CanApply = false;
            _handler.PendingRequest = ReducerRequest.Apply;
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void CopyAllLogs()
        {
            if (Logs.Count == 0) return;
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, Logs.Select(l => l.ToString())));
        }

        [RelayCommand]
        private void ClearLogs() => Logs.Clear();

        public void CopySelectedLogs(IList selected)
        {
            if (selected == null || selected.Count == 0) return;
            var lines = selected.Cast<LogEntry>().Select(l => l.ToString());
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        /// <summary>Called from the event handler after a floor pick completes.</summary>
        public void SetSelectedFloor(string label)
        {
            FloorLabel = label;
            PreviewRows.Clear();
            CanPreview = true;
            CanApply = false;
            SummaryText = "No preview run yet";
        }

        /// <summary>Called from the event handler after ClassifyAndReduce completes.</summary>
        public void PopulatePreview(List<VertexDecision> decisions)
        {
            PreviewRows.Clear();
            int idx = 0;
            foreach (var d in decisions.OrderBy(d => d.SegmentLabel))
            {
                idx++;
                PreviewRows.Add(new VertexPreviewRow
                {
                    Segment = d.SegmentLabel,
                    PointId = $"#{idx}",
                    Z = $"{EdgeVertexReducerService.FeetToMm(d.ZFeet):0.0} mm",
                    Action = ActionText(d),
                    ActionColorKey = ActionColorKey(d)
                });
            }
            CanApply = true;
        }

        private static string ActionText(VertexDecision d) => d.Action switch
        {
            VertexAction.KeepStart => "Keep (start)",
            VertexAction.KeepEnd => "Keep (end)",
            VertexAction.KeepMaxZ => "Keep (max Z)",
            VertexAction.KeepNeighbor => "Keep (neighbor)",
            VertexAction.KeepUnmatched => "Keep (unmatched)",
            VertexAction.Remove => "Remove",
            _ => "â€”"
        };

        private static string ActionColorKey(VertexDecision d) =>
            d.Action == VertexAction.Remove ? "Danger" :
            d.Action == VertexAction.KeepMaxZ ? "Success" :
            d.Action == VertexAction.KeepNeighbor ? "Success" : "Secondary";

        public void AddLog(LogLevel level, string message) => Logs.Add(new LogEntry(level, message));

        public void SetSummary(string text) => SummaryText = text;
    }
}

