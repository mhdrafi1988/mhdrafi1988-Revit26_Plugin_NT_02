// =======================================================
// File: RoofPointsAndSlopeEngine.cs
// Location: Core/Engine/
// Runs the enabled steps on one roof, in order:
//   1–4  point steps — planned again from the roof's current shape points
//        (so the result matches the preview even if points changed since),
//        each written in its own transaction by ShapePointWriter.
//   5    ridge — Ridge By Openings V070's services, unchanged (planned by
//        RidgePlanner): shape points + split lines along the ridges
//        midway between the drains, and/or detail lines in the active view.
//   6    slope by drain — Auto Slope By Drain V013's engine, unchanged:
//        it resets every shape point to 0, matches each selected drain to
//        the shape points on its edge (the ones step 1 added), sets every
//        point's height from the shortest path to a drain (the ridge points
//        are the farthest, so they end up highest), and writes the
//        roof parameters (and the Excel report when asked).
// The handler opens one TransactionGroup around all of this and keeps it
// only when Execute returns Success, so a failure anywhere rolls the
// whole run back (one Undo step either way).
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Engine;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Planning;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Models;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Engine
{
    /// <summary>Runs the six steps (see the file header).</summary>
    public static class RoofPointsAndSlopeEngine
    {
        /// <summary>Display names of the point steps, in run order.</summary>
        public static readonly IReadOnlyDictionary<PointStep, string> StepNames = new Dictionary<PointStep, string>
        {
            [PointStep.OpeningPoints] = "Opening points",
            [PointStep.Perpendicular] = "Perpendicular points",
            [PointStep.MidQuarter] = "Midpoint / quarter points",
            [PointStep.GapFill] = "Gap fill"
        };

        /// <summary>
        /// Checks the roof can be changed: it exists, is in the active document, is not in a group
        /// and is not owned by another user. Returns the roof, or null with <paramref name="error"/> set.
        /// </summary>
        public static FootPrintRoof ValidateRoof(UIApplication app, Document expectedDoc, ElementId roofId, out string error)
        {
            error = null;
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                error = "No document is open.";
                return null;
            }

            if (expectedDoc != null && expectedDoc.IsValidObject && !doc.Equals(expectedDoc))
            {
                error = $"Switch back to \"{expectedDoc.Title}\" (the roof's document) and run again.";
                return null;
            }

            if (doc.GetElement(roofId) is not FootPrintRoof roof)
            {
                error = "The roof could not be found (it may have been deleted). Close the tool and pick the roof again.";
                return null;
            }

            if (roof.GroupId != ElementId.InvalidElementId)
            {
                error = "The roof is in a group. Ungroup it (or edit the group) and run again.";
                return null;
            }

            if (doc.IsWorkshared &&
                WorksharingUtils.GetCheckoutStatus(doc, roof.Id) == CheckoutStatus.OwnedByOtherUser)
            {
                error = "The roof is being edited by another user. Ask them to release it (synchronise), then run again.";
                return null;
            }

            return roof;
        }

        /// <summary>Runs the enabled steps. Call inside a TransactionGroup; keep it only on Success.</summary>
        public static RunResult Execute(UIApplication app, RunPayload payload)
        {
            var log = payload.Log ?? (_ => { });
            var progress = payload.Progress ?? ((_, __) => { });
            var result = new RunResult();

            FootPrintRoof roof = ValidateRoof(app, payload.Document, payload.RoofId, out string error);
            if (roof == null) return Fail(result, log, error);
            Document doc = roof.Document;
            PipelineSettings settings = payload.Settings;

            // ── Steps 1–4 ────────────────────────────────────────────────────
            payload.Outline.ExistingVertices = RoofReader.ReadShapePoints(roof);
            PipelinePlan plan = PointPipelinePlanner.Plan(payload.Outline, settings);

            int stepNo = 0;
            foreach (StepPlan step in plan.Steps)
            {
                stepNo++;
                if (step.Points.Count == 0) continue;

                string name = StepNames[step.Step];
                progress(stepNo * 15, $"Step {stepNo}: {name}");
                ShapePointWriteResult w = ShapePointWriter.Write(doc, roof, step.Points, $"Roof Points & Slope – {name}");
                if (!w.Success)
                    return Fail(result, log, $"Step {stepNo} ({name}) failed: {w.ErrorMessage}");

                result.AddedPerStep[name] = w.Added;
                result.TotalAdded += w.Added;
                result.TotalRejected += w.Rejected;

                string nudged = w.Nudged > 0 ? $", {w.Nudged} after a small nudge into the roof" : string.Empty;
                string there = w.AlreadyThere > 0 ? $", {w.AlreadyThere} already there" : string.Empty;
                log(new LogEntry(LogLevel.Success, $"Step {stepNo} · {name}: {w.Added} point(s) added{nudged}{there}."));
                if (w.Rejected > 0)
                    log(new LogEntry(LogLevel.Warning,
                        $"Step {stepNo} · {name}: Revit rejected {w.Rejected} point(s) — usually a point outside the roof's top surface."));
            }

            bool anyPointStep = settings.InnerPointsEnabled || settings.PerpendicularEnabled ||
                                settings.MidQuarterEnabled || settings.GapFillEnabled;
            if (anyPointStep && result.TotalAdded == 0)
                log(new LogEntry(LogLevel.Info, "Point steps: nothing new to add — every planned point is already there."));

            // ── Step 5: ridge ────────────────────────────────────────────────
            if (settings.RidgeEnabled)
            {
                progress(62, "Step 5: Ridge");
                if (!RunRidge(app, doc, roof, payload, settings, result, log))
                    return result;
            }

            // ── Step 6: slope ────────────────────────────────────────────────
            if (settings.SlopeEnabled)
            {
                progress(70, "Step 6: Slope by drain");
                var drains = payload.SelectedDrains ?? new List<DrainItem>();
                if (drains.Count == 0)
                    return Fail(result, log, "Step 6 (Slope by drain): no drain is ticked. Tick at least one opening as a drain, or turn step 6 off.");

                log(new LogEntry(LogLevel.Info, $"Step 6 · Slope by drain: {drains.Count} drain(s), {settings.SlopePercent} % (Auto Slope By Drain V013 engine)."));
                var slopePayload = new AutoSlopeDrainPayload
                {
                    RoofId = roof.Id,
                    RoofName = roof.Name,
                    SelectedDrains = drains,
                    TotalDetectedCount = payload.DetectedDrainCount,
                    SlopePercent = settings.SlopePercent,
                    ConnectionThresholdMeters = settings.MaxEdgeMeters,
                    ThresholdMeters = settings.MaxPathMeters,
                    PathSampleCount = settings.PathSamples,
                    InsertCurveIntersectionPoints = settings.InsertCurveIntersectionPoints,
                    VerifyElevationsAfterCommit = false,
                    ExportConfig = settings.ExportToExcel
                        ? new ExportConfig { ExportPath = settings.ExportFolder, ExportToExcel = true }
                        : null,
                    ProjectTitle = doc.Title,
                    CancelToken = CancellationToken.None,
                    Log = entry => log(new LogEntry(entry.Level, "Step 6 · " + entry.Message)),
                    Progress = info => progress(70 + 0.25 * (info?.PercentWithinPhase ?? 50), "Step 6: " + info?.PhaseLabel)
                };

                AutoSlopeDrainResult slope = AutoSlopeDrainEngine.Execute(app, slopePayload);
                if (!slope.Success)
                    return Fail(result, log, "Step 6 (Slope by drain) failed: " + (slope.ErrorMessage ?? "see the log above."));

                result.Sloped = true;
                result.HighestElevationMm = slope.HighestElevation_mm;
            }

            result.VerticesAfter = RoofReader.ReadShapePoints(roof);
            result.Success = true;
            progress(100, "Done");
            return result;
        }

        /// <summary>Step 5. Returns false (with the result marked failed) when the ridge can't be made.</summary>
        private static bool RunRidge(UIApplication app, Document doc, RoofBase roof, RunPayload payload,
                                     PipelineSettings settings, RunResult result, Action<LogEntry> log)
        {
            VoronoiRidgeResult ridge = RidgePlanner.Plan(payload.RidgeInput, payload.DrainOpenings, settings, out string error);
            if (ridge == null)
            {
                Fail(result, log, "Step 5 (Ridge): " + error);
                return false;
            }

            bool lines = settings.RidgeDetailLines;
            View view = app.ActiveUIDocument?.ActiveView;
            if (lines && !CanHoldDetailLines(view))
            {
                lines = false;
                log(new LogEntry(LogLevel.Warning,
                    "Step 5 · Ridge: the active view can't hold detail lines (open a plan view for them) — ridge lines skipped."));
            }

            if (!lines && !settings.RidgeShapePoints)
            {
                log(new LogEntry(LogLevel.Info, "Step 5 · Ridge: nothing to create (no shape points, no detail lines)."));
                return true;
            }

            var creation = new RidgeCreationService
            {
                OnSlabWarning = message => log(new LogEntry(LogLevel.Info, "Step 5 · " + message))
            };
            ElementId styleId = RidgeCreationService.GetLineStyleIdByName(doc, settings.RidgeLineStyle);
            creation.CreateAll(doc, view, roof, ridge, lines, settings.RidgeShapePoints, styleId);

            result.RidgeLines = ridge.CreatedDetailLineIds.Count;
            result.TotalAdded += creation.TotalAdded;
            result.AddedPerStep["Ridge"] = creation.TotalAdded;
            log(new LogEntry(LogLevel.Success,
                $"Step 5 · Ridge: {ridge.ClippedEdges.Count} ridge line(s) between {ridge.DrainGroups.Count} drain group(s)" +
                (settings.RidgeShapePoints ? $", {creation.TotalAdded} of {creation.TotalCalculated} point(s) added with split lines" : string.Empty) +
                (lines ? $", {ridge.CreatedDetailLineIds.Count} detail line(s) drawn" : string.Empty) + "."));
            if (creation.TotalFailed > 0)
                log(new LogEntry(LogLevel.Warning, $"Step 5 · Ridge: {creation.TotalFailed} point(s) could not be added."));
            if (ridge.FailCount > 0)
                log(new LogEntry(LogLevel.Warning,
                    $"Step 5 · Ridge: {ridge.FailCount} of {ridge.ValidationLog.Count} ridge point(s) are not equidistant from their drains within {settings.RidgeValidationToleranceMm:0} mm."));
            return true;
        }

        /// <summary>Plan, section, elevation, detail and drafting views hold detail lines; 3D views, sheets and schedules don't.</summary>
        private static bool CanHoldDetailLines(View view) =>
            view != null && !view.IsTemplate &&
            view.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.AreaPlan
                or ViewType.Section or ViewType.Elevation or ViewType.Detail or ViewType.DraftingView;

        private static RunResult Fail(RunResult result, Action<LogEntry> log, string message)
        {
            log(new LogEntry(LogLevel.Error, message));
            result.Success = false;
            result.ErrorMessage = message;
            return result;
        }
    }
}
