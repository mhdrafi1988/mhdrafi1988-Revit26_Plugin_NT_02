// =======================================================
// File: FloorDetailLineIntersectViewModel.cs
// Location: UI/ViewModels/
// Changes vs V008:
//   MOVED all geometry/placement logic out to
//   Core/Engine/FloorDetailLineIntersectEngine.cs (see that file's
//   header) â€” this suite's vertical-slice convention keeps Core/Engine
//   logic out of UI/ViewModels.
//   The private inner IExternalEventHandler class + directly-held
//   Document/Floor/List<DetailLine> fields are replaced by the
//   standard Payload/Handler/EventManager pattern used by every other
//   tool â€” FloorId/DetailLineIds (ElementIds) are carried across the
//   boundary and re-resolved inside the Handler, rather than holding
//   live Element references across multiple Run clicks.
//   [RelayCommand] attributes replace the hand-wired
//   ICommand/RelayCommand-in-constructor pattern (was already
//   ObservableObject-based, just not using the attributes).
// Changes vs V011:
//   Added IncludeLineToLineIntersections (default true) + its result
//   counter LineToLineIntersectionsFound â€” new option to also place
//   shape points where selected detail lines cross each other, not
//   just where they cross the floor boundary.
// =======================================================

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Core.Models;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.UI.ViewModels
{
    public partial class FloorDetailLineIntersectViewModel : ObservableObject
    {
        private readonly ElementId _floorId;
        private readonly List<ElementId> _detailLineIds;

        [ObservableProperty] private string floorName   = string.Empty;
        [ObservableProperty] private string floorIdText = string.Empty;
        [ObservableProperty] private string viewName   = string.Empty;
        [ObservableProperty] private int    totalLines;
        [ObservableProperty] private int    intersectionsFound;
        [ObservableProperty] private int    lineToLineIntersectionsFound;
        [ObservableProperty] private int    pointsPlaced;
        [ObservableProperty] private int    skippedCount;

        /// <summary>New in V012 â€” also place points where selected detail lines cross each other.</summary>
        [ObservableProperty] private bool   includeLineToLineIntersections = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool isBusy;

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ──
        [ObservableProperty] private double progress;
        [ObservableProperty] private string summaryText = string.Empty;

        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public FloorDetailLineIntersectViewModel(
            ElementId floorId, List<ElementId> detailLineIds, string floorName, string viewName)
        {
            _floorId        = floorId;
            _detailLineIds = detailLineIds;

            FloorName   = floorName ?? "Floor";
            FloorIdText = $"id {floorId.Value}";
            ViewName   = viewName ?? string.Empty;
            TotalLines = detailLineIds.Count;

            FloorDetailLineIntersectEventManager.Init();
        }

        // â”€â”€ Trigger ExternalEvent â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            IsBusy = true;
            LogEntries.Clear();
            IntersectionsFound           = 0;
            LineToLineIntersectionsFound = 0;
            PointsPlaced                 = 0;
            SkippedCount                 = 0;

            FloorDetailLineIntersectHandler.Payload = new FloorDetailLineIntersectPayload
            {
                FloorId                         = _floorId,
                DetailLineIds                  = _detailLineIds,
                IncludeLineToLineIntersections = IncludeLineToLineIntersections,
                Log                            = AddLog,
                OnCompleted                    = result =>
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsBusy = false;

                        if (!result.Success)
                        {
                            AddLog(LogLevel.Error, $"Run failed: {result.ErrorMessage}");
                            return;
                        }

                        IntersectionsFound           = result.IntersectionsFound;
                        LineToLineIntersectionsFound = result.LineToLineIntersectionsFound;
                        PointsPlaced                 = result.PointsPlaced;
                        SkippedCount                 = result.SkippedCount;
                    }));
                }
            };

            try
            {
                FloorDetailLineIntersectEventManager.Event.Raise();
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Fatal error raising event: {ex.Message}");
                IsBusy = false;
            }
        }

        private bool CanRun() => !IsBusy;

        // â”€â”€ Log & Copy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand]
        private void CopyLog()
        {
            if (LogEntries.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var entry in LogEntries)
                sb.AppendLine(entry.ToString());
            System.Windows.Clipboard.SetText(sb.ToString());
        }

        private void AddLog(LogLevel level, string message) => AddLog(new LogEntry(level, message));

        private void AddLog(LogEntry entry)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
                LogEntries.Add(entry);
            else
                dispatcher.BeginInvoke(new Action(() => LogEntries.Add(entry)));
        }
    }
}

