// =======================================================
// File: FloorPointsAndSlopeHandler.cs
// Location: Infrastructure/ExternalEvents/
// One handler, two requests:
//   Analyze — re-reads the floor (read-only, no transaction).
//   Run     — one TransactionGroup around every step; kept only when the
//             engine succeeds, so a failure rolls the whole run back and
//             a success is a single Undo step.
// The body goes through ToolGuard.RunHandler; the request is cleared
// before it runs so a stray Raise() can't replay it.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Services;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Infrastructure.ExternalEvents
{
    /// <summary>Runs Analyze and Run requests in Revit's API context.</summary>
    public sealed class FloorPointsAndSlopeHandler : IExternalEventHandler
    {
        /// <summary>Set by the view model immediately before raising the event.</summary>
        public static RunPayload Payload;

        /// <inheritdoc/>
        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private static void ExecuteCore(UIApplication app)
        {
            RunPayload current = Payload;
            Payload = null;
            if (current == null) return;

            RunResult result = null;
            // Esc / Cancel with a Yes-No confirm while Analyze or Run is working (markers are quick).
            using ToolCancellation cancel = current.Operation is RunOperation.ShowMarkers or RunOperation.ClearMarkers
                ? null
                : ToolCancellation.BeginFor(ToolCatalog.FloorPointsAndSlope, app);
            try
            {
                result = current.Operation switch
                {
                    RunOperation.Analyze => Analyze(app, current),
                    RunOperation.ShowMarkers => ShowMarkers(app, current),
                    RunOperation.ClearMarkers => ClearMarkers(current),
                    _ => Run(app, current)
                };
            }
            catch (OperationCanceledException)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                result = new RunResult { Success = false, WasCancelled = true, ErrorMessage = "Cancelled." };
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Error, $"Unexpected error: {ex.Message}. Nothing was changed."));
                result = new RunResult { Success = false, ErrorMessage = ex.Message };
                throw; // ToolGuard logs it and shows the error dialog.
            }
            finally
            {
                current.OnCompleted?.Invoke(result ?? new RunResult { Success = false, ErrorMessage = "Stopped." });
            }
        }

        private static RunResult Analyze(UIApplication app, RunPayload current)
        {
            Floor floor = FloorPointsAndSlopeEngine.ValidateFloor(app, current.Document, current.FloorId, out string error);
            if (floor == null)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Error, error));
                return new RunResult { Success = false, ErrorMessage = error };
            }

            return new RunResult
            {
                Success = true,
                Outline = ReadOutline(floor, current.Log),
                RidgeInput = ReadRidgeInput(floor, current.Log)
            };
        }

        /// <summary>Shows markers in the active view, at the floor's top, and zooms when asked.</summary>
        private static RunResult ShowMarkers(UIApplication app, RunPayload current)
        {
            Floor floor = FloorPointsAndSlopeEngine.ValidateFloor(app, current.Document, current.FloorId, out string error, checkOwnership: false);
            if (floor == null)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Error, error));
                return new RunResult { Success = false, ErrorMessage = error };
            }

            UIDocument uidoc = app.ActiveUIDocument;
            View view = uidoc.ActiveView;
            if (view == null || view.IsTemplate || view.ViewType is ViewType.Schedule or ViewType.DrawingSheet or ViewType.ProjectBrowser or ViewType.SystemBrowser)
            {
                const string msg = "Open a plan or 3D view to see the markers.";
                current.Log?.Invoke(new LogEntry(LogLevel.Warning, msg));
                return new RunResult { Success = false, ErrorMessage = msg };
            }

            BoundingBoxXYZ box = floor.get_BoundingBox(null);
            double z = box?.Max.Z ?? 0;
            int shown = MarkerService.Show(floor.Document, view, current.Markers ?? new List<MarkerPoint>(), z);

            if (current.ZoomTo is { } zoom)
            {
                UIView uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
                double pad = Math.Max(3, Math.Max(zoom.Max.X - zoom.Min.X, zoom.Max.Y - zoom.Min.Y)); // feet
                uiView?.ZoomAndCenterRectangle(new XYZ(zoom.Min.X - pad, zoom.Min.Y - pad, z), new XYZ(zoom.Max.X + pad, zoom.Max.Y + pad, z));
            }

            return new RunResult { Success = true, MarkersShown = shown };
        }

        private static RunResult ClearMarkers(RunPayload current)
        {
            MarkerService.Clear(current.Document);
            return new RunResult { Success = true };
        }

        private static RunResult Run(UIApplication app, RunPayload current)
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new RunResult { Success = false, ErrorMessage = "No document is open." };

            MarkerService.Clear(current.Document); // markers belong to the plan the run replaces

            List<RunPayload> floors = current.Floors ?? new List<RunPayload> { current };
            string name = floors.Count > 1 ? $"Floor Points & Slope ({floors.Count} floors)" : "Floor Points & Slope";
            var total = new RunResult { Success = true };

            using (var tg = new TransactionGroup(doc, name))
            {
                tg.Start();
                try
                {
                foreach (RunPayload floor in floors)
                {
                    ToolCancellation.CheckCurrent();
                    RunResult r = FloorPointsAndSlopeEngine.Execute(app, floor);
                    r.FloorId = floor.FloorId;
                    total.PerFloor.Add(r);
                    if (!r.Success)
                    {
                        total.Success = false;
                        total.WasCancelled = r.WasCancelled;
                        total.ErrorMessage = floors.Count > 1 ? $"{floor.FloorLabel}: {r.ErrorMessage}" : r.ErrorMessage;
                        break;
                    }

                    total.TotalAdded += r.TotalAdded;
                    total.TotalRejected += r.TotalRejected;
                    total.RidgeLines += r.RidgeLines;
                    total.Sloped |= r.Sloped;
                    total.HighestElevationMm = Math.Max(total.HighestElevationMm, r.HighestElevationMm);
                }
                }
                catch (OperationCanceledException)
                {
                    // Undo the whole run, then let ToolGuard report the cancel.
                    if (tg.GetStatus() == TransactionStatus.Started) tg.RollBack();
                    throw;
                }
                ToolCancellation.CheckCurrent();

                if (total.Success)
                {
                    tg.Assimilate();
                }
                else
                {
                    tg.RollBack();
                    current.Log?.Invoke(new LogEntry(LogLevel.Info, floors.Count > 1
                        ? "Rolled back: every floor is exactly as it was before Run."
                        : "Rolled back: the floor is exactly as it was before Run."));
                }

                return total;
            }
        }

        /// <summary>Reads the floor and matches it with Floor Slope By Drain V001's opening detection.</summary>
        public static FloorOutline ReadOutline(Floor floor, Action<LogEntry> log)
        {
            List<DrainItem> drains;
            try
            {
                // Floor Slope By Drain V001's detection logs a "DRAIN-DEBUG" summary; only its warnings and errors matter here.
                drains = new DrainDetectionService().DetectDrainsFromFloor(floor, null, null,
                    entry => { if (entry.Level is LogLevel.Warning or LogLevel.Error) log?.Invoke(entry); });
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning, $"Drain detection failed ({ex.Message}); openings can't be used as drains."));
                drains = new List<DrainItem>();
            }

            FloorOutline outline = FloorReader.Read(floor, drains);
            outline.DetectedDrainCount = drains.Count;
            return outline;
        }

        /// <summary>
        /// Reads what the ridge step needs with Ridge By Openings V070's own readers (boundary,
        /// openings) plus the document's line styles. Never throws: a failure is logged and the
        /// ridge step then reports it can't run.
        /// </summary>
        public static RidgeInput ReadRidgeInput(Floor floor, Action<LogEntry> log)
        {
            List<string> styles;
            try { styles = FloorRidgeCreationService.GetAvailableLineStyles(floor.Document).Select(s => s.Name).ToList(); }
            catch { styles = new List<string>(); }

            try
            {
                return new RidgeInput
                {
                    Boundary = new FloorBoundaryService().ExtractBoundary(floor) ?? new List<XYZ>(),
                    InnerLoops = new FloorInnerLoopService().ExtractInnerLoops(floor) ?? new List<List<XYZ>>(),
                    LineStyles = styles
                };
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning, $"The ridge step could not read the floor ({ex.Message}); it can't run on this floor."));
                return new RidgeInput { LineStyles = styles };
            }
        }

        /// <inheritdoc/>
        public string GetName() => "Floor Points & Slope";
    }

    /// <summary>One handler and one ExternalEvent for the tool, created once.</summary>
    public static class FloorPointsAndSlopeEventManager
    {
        /// <summary>The handler.</summary>
        public static FloorPointsAndSlopeHandler Handler { get; private set; }

        /// <summary>The event.</summary>
        public static ExternalEvent Event { get; private set; }

        /// <summary>Creates the handler and event on first use. Call from a valid API context (the command).</summary>
        public static void Init()
        {
            if (Handler != null) return;
            Handler = new FloorPointsAndSlopeHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
