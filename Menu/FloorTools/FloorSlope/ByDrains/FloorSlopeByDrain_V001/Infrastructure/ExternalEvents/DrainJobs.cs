// =======================================================
// File: DrainJobs.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.ExternalEvents
// New in V013. One handler runs one of three jobs:
//   DrainRunJob   — the multi-floor run. One outer TransactionGroup (one
//                   Undo step for the whole run) and, inside it, one group
//                   per floor. A floor's group is assimilated only when that
//                   floor succeeds; a failed or cancelled floor is rolled back
//                   completely (V012 kept whatever sub-steps had already
//                   committed, e.g. inserted arc points or parameters).
//                   Cancel scope is unchanged from V010: floors finished
//                   before the cancel stay applied, later floors never start.
//   FocusFloorJob  — select a floor and show it (floor tab switch). V012 did
//                   this straight from the WPF event, outside Revit's API
//                   context.
//   ZoomJob       — select the floor and zoom the active view to the rows
//                   chosen in the results panel.
// =======================================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.ExternalEvents
{
    public abstract class DrainJob
    {
        public abstract void Execute(UIApplication app);
    }

    public sealed class DrainRunJob : DrainJob
    {
        private readonly AutoSlopeDrainMultiPayload _batch;

        public DrainRunJob(AutoSlopeDrainMultiPayload batch) => _batch = batch;

        public override void Execute(UIApplication app)
        {
            var log = _batch.Log ?? (_ => { });
            var floorResults = new List<AutoSlopeDrainFloorResult>();

            // Esc / Cancel with a Yes-No confirm. A cancel undoes the WHOLE run (every floor), not only the
            // floor in progress; Excel files already written are kept.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.FloorSlopeByDrain, app);

            try
            {
                Document doc = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No document is open.");

                bool anySuccess = false;
                bool cancelled = false;
                using (var outer = new TransactionGroup(doc, "Floor Slope By Drain"))
                {
                    outer.Start();

                    foreach (AutoSlopeDrainPayload floorPayload in _batch.FloorPayloads ?? new List<AutoSlopeDrainPayload>())
                    {
                        if (floorPayload.CancelToken.IsCancellationRequested || cancel.IsCancelled) break; // later floors never start
                        ToolCancellation.CheckCurrent();

                        AutoSlopeDrainResult result;
                        using (var perFloor = new TransactionGroup(doc, $"Floor Slope By Drain – {floorPayload.FloorName}"))
                        {
                            perFloor.Start();
                            result = AutoSlopeDrainEngine.Execute(app, floorPayload);

                            if (result.Success)
                            {
                                perFloor.Assimilate();
                                anySuccess = true;
                            }
                            else
                            {
                                perFloor.RollBack();
                                floorPayload.Log?.Invoke(new LogEntry(LogLevel.Info,
                                    "Rolled back: this floor is exactly as it was before Run."));
                            }
                        }

                        floorResults.Add(new AutoSlopeDrainFloorResult
                        {
                            FloorName = floorPayload.FloorName,
                            FloorElementId = floorPayload.FloorId?.Value ?? 0,
                            Result = result
                        });
                        floorPayload.OnCompleted?.Invoke(result);

                        if (result.WasCancelled) break;
                    }

                    cancelled = cancel.IsCancelled || floorResults.Any(r => r.Result?.WasCancelled == true);
                    if (cancelled)
                    {
                        // Undo-all: floors that had already finished are rolled back too.
                        outer.RollBack();
                        foreach (var r in floorResults.Where(r => r.Result?.Success == true))
                        {
                            r.Result.Success = false;
                            r.Result.WasCancelled = true;
                            r.Result.ErrorMessage = "Rolled back: the run was cancelled.";
                        }
                    }
                    else if (anySuccess) outer.Assimilate();
                    else outer.RollBack();
                }

                if (!cancelled && _batch.WriteCombinedSummary && floorResults.Count(r => r.Result?.Success == true) > 1)
                {
                    string combinedPath = ExcelExportService.ExportCombinedSummary(
                        floorResults, _batch.ExportFolderPath, _batch.ProjectTitle, log);
                    if (!string.IsNullOrEmpty(combinedPath))
                        log(new LogEntry(LogLevel.Success, $"Combined summary saved: {combinedPath}"));
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled between floors: the using blocks rolled every group back.
                foreach (var r in floorResults.Where(r => r.Result != null))
                {
                    r.Result.Success = false;
                    r.Result.WasCancelled = true;
                    r.Result.ErrorMessage = "Rolled back: the run was cancelled.";
                }
                _batch.OnAllCompleted?.Invoke(floorResults);
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                // Unexpected (the engine catches its own errors): the using blocks have
                // already rolled back every group, including floors that had finished.
                log(new LogEntry(LogLevel.Error, $"Unexpected error: {ex.Message}. Every floor in this run was rolled back."));
                foreach (var r in floorResults.Where(r => r.Result != null))
                {
                    r.Result.Success = false;
                    r.Result.ErrorMessage = "Rolled back after an unexpected error.";
                }
                _batch.OnAllCompleted?.Invoke(floorResults);
                throw; // ToolGuard.RunHandler logs it and shows the error dialog.
            }

            _batch.OnAllCompleted?.Invoke(floorResults);
        }
    }

    public sealed class FocusFloorJob : DrainJob
    {
        private readonly long _floorIdValue;

        public FocusFloorJob(long floorIdValue) => _floorIdValue = floorIdValue;

        public override void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;

            var id = new ElementId(_floorIdValue);
            if (uidoc.Document.GetElement(id) == null) return;

            var ids = new List<ElementId> { id };
            uidoc.Selection.SetElementIds(ids);
            uidoc.ShowElements(ids);
        }
    }

    public sealed class ZoomJob : DrainJob
    {
        private readonly long _floorIdValue;
        private readonly IReadOnlyList<XYZ> _points;
        private readonly Action<LogLevel, string> _log;

        public ZoomJob(long floorIdValue, IReadOnlyList<XYZ> points, Action<LogLevel, string> log)
        {
            _floorIdValue = floorIdValue;
            _points = points;
            _log = log ?? ((_, __) => { });
        }

        public override void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || _points == null || _points.Count == 0) return;

            View view = uidoc.ActiveView;
            UIView uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view?.Id);
            if (uiView == null)
            {
                _log(LogLevel.Warning, "Zoom to: the active view cannot be zoomed. Open a plan or 3D view and try again.");
                return;
            }

            double pad = 2.0 / 0.3048; // 2 m
            var min = new XYZ(_points.Min(p => p.X) - pad, _points.Min(p => p.Y) - pad, _points.Min(p => p.Z) - pad);
            var max = new XYZ(_points.Max(p => p.X) + pad, _points.Max(p => p.Y) + pad, _points.Max(p => p.Z) + pad);

            uidoc.Selection.SetElementIds(new List<ElementId> { new ElementId(_floorIdValue) });
            uiView.ZoomAndCenterRectangle(min, max);
            _log(LogLevel.Info, $"Zoomed to {_points.Count} point(s) in \"{view.Name}\" and selected the floor.");
        }
    }
}
