// =======================================================
// File: RoofPointsAndSlopeHandler.cs
// Location: Infrastructure/ExternalEvents/
// One handler, two requests:
//   Analyze — re-reads the roof (read-only, no transaction).
//   Run     — one TransactionGroup around every step; kept only when the
//             engine succeeds, so a failure rolls the whole run back and
//             a success is a single Undo step.
// The body goes through ToolGuard.RunHandler; the request is cleared
// before it runs so a stray Raise() can't replay it.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Models;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Services;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Engine;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.ExternalEvents
{
    /// <summary>Runs Analyze and Run requests in Revit's API context.</summary>
    public sealed class RoofPointsAndSlopeHandler : IExternalEventHandler
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
            try
            {
                result = current.Operation == RunOperation.Analyze ? Analyze(app, current) : Run(app, current);
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
            FootPrintRoof roof = RoofPointsAndSlopeEngine.ValidateRoof(app, current.Document, current.RoofId, out string error);
            if (roof == null)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Error, error));
                return new RunResult { Success = false, ErrorMessage = error };
            }

            return new RunResult
            {
                Success = true,
                Outline = ReadOutline(roof, current.Log),
                RidgeInput = ReadRidgeInput(roof, current.Log)
            };
        }

        private static RunResult Run(UIApplication app, RunPayload current)
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new RunResult { Success = false, ErrorMessage = "No document is open." };

            using (var tg = new TransactionGroup(doc, "Roof Points & Slope"))
            {
                tg.Start();
                RunResult result = RoofPointsAndSlopeEngine.Execute(app, current);
                if (result.Success)
                {
                    tg.Assimilate();
                }
                else
                {
                    tg.RollBack();
                    current.Log?.Invoke(new LogEntry(LogLevel.Info, "Rolled back: the roof is exactly as it was before Run."));
                }

                return result;
            }
        }

        /// <summary>Reads the roof and matches it with Auto Slope By Drain V013's opening detection.</summary>
        public static RoofOutline ReadOutline(FootPrintRoof roof, Action<LogEntry> log)
        {
            List<DrainItem> drains;
            try
            {
                // V013's detection logs a "DRAIN-DEBUG" summary; only its warnings and errors matter here.
                drains = new DrainDetectionService().DetectDrainsFromRoof(roof, null, null,
                    entry => { if (entry.Level is LogLevel.Warning or LogLevel.Error) log?.Invoke(entry); });
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning, $"Drain detection failed ({ex.Message}); openings can't be used as drains."));
                drains = new List<DrainItem>();
            }

            RoofOutline outline = RoofReader.Read(roof, drains);
            outline.DetectedDrainCount = drains.Count;
            return outline;
        }

        /// <summary>
        /// Reads what the ridge step needs with Ridge By Openings V070's own readers (boundary,
        /// openings) plus the document's line styles. Never throws: a failure is logged and the
        /// ridge step then reports it can't run.
        /// </summary>
        public static RidgeInput ReadRidgeInput(RoofBase roof, Action<LogEntry> log)
        {
            List<string> styles;
            try { styles = RidgeCreationService.GetAvailableLineStyles(roof.Document).Select(s => s.Name).ToList(); }
            catch { styles = new List<string>(); }

            try
            {
                return new RidgeInput
                {
                    Boundary = new RoofBoundaryService().ExtractBoundary(roof) ?? new List<XYZ>(),
                    InnerLoops = new InnerLoopService().ExtractInnerLoops(roof) ?? new List<List<XYZ>>(),
                    LineStyles = styles
                };
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning, $"The ridge step could not read the roof ({ex.Message}); it can't run on this roof."));
                return new RidgeInput { LineStyles = styles };
            }
        }

        /// <inheritdoc/>
        public string GetName() => "Roof Points & Slope";
    }

    /// <summary>One handler and one ExternalEvent for the tool, created once.</summary>
    public static class RoofPointsAndSlopeEventManager
    {
        /// <summary>The handler.</summary>
        public static RoofPointsAndSlopeHandler Handler { get; private set; }

        /// <summary>The event.</summary>
        public static ExternalEvent Event { get; private set; }

        /// <summary>Creates the handler and event on first use. Call from a valid API context (the command).</summary>
        public static void Init()
        {
            if (Handler != null) return;
            Handler = new RoofPointsAndSlopeHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
