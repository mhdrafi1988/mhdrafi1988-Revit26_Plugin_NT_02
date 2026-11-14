// =======================================================
// File: FloorPointsAndSlopeEngine.cs
// Location: Core/Engine/
// Runs the enabled steps on one floor, in order:
//   1–4  point steps — planned again from the floor's current shape points
//        (so the result matches the preview even if points changed since),
//        each written in its own transaction by ShapePointWriter.
//   5    ridge — Ridge By Openings V070's services, unchanged (planned by
//        RidgePlanner): shape points + split lines along the ridges
//        midway between the drains, and/or detail lines in the active view.
//   6    slope by drain — Floor Slope By Drain V001's engine, unchanged:
//        it resets every shape point to 0, matches each selected drain to
//        the shape points on its edge (the ones step 1 added), sets every
//        point's height from the shortest path to a drain (the ridge points
//        are the farthest, so they end up highest), and writes the
//        floor parameters (and the Excel report when asked).
// The handler opens one TransactionGroup around all of this and keeps it
// only when Execute returns Success, so a failure anywhere rolls the
// whole run back (one Undo step either way).
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Planning;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Models;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Engine
{
    /// <summary>Runs the six steps (see the file header).</summary>
    public static class FloorPointsAndSlopeEngine
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
        /// Checks the floor can be changed: it exists, is in the active document, is not in a group
        /// and is not owned by another user. Returns the floor, or null with <paramref name="error"/> set.
        /// <paramref name="checkOwnership"/> false skips the group and checkout checks (read-only use).
        /// </summary>
        public static Floor ValidateFloor(UIApplication app, Document expectedDoc, ElementId floorId, out string error,
                                                 bool checkOwnership = true)
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
                error = $"Switch back to \"{expectedDoc.Title}\" (the floor's document) and run again.";
                return null;
            }

            if (doc.GetElement(floorId) is not Floor floor)
            {
                error = "The floor could not be found (it may have been deleted). Close the tool and pick the floor again.";
                return null;
            }

            if (!checkOwnership) return floor; // read-only use (markers): group and checkout don't matter

            if (floor.GroupId != ElementId.InvalidElementId)
            {
                error = "The floor is in a group. Ungroup it (or edit the group) and run again.";
                return null;
            }

            // Floors only: shape-editing support and read-only model (FloorShapeEditChecks).
            string blockReason = FloorShapeEditChecks.GetBlockReason(floor);
            if (blockReason != null)
            {
                error = $"The floor can't be changed: {blockReason}.";
                return null;
            }

            if (doc.IsWorkshared &&
                WorksharingUtils.GetCheckoutStatus(doc, floor.Id) == CheckoutStatus.OwnedByOtherUser)
            {
                error = "The floor is being edited by another user. Ask them to release it (synchronise), then run again.";
                return null;
            }

            return floor;
        }

        /// <summary>
        /// Runs the enabled steps. Call inside a TransactionGroup; keep it only on Success.
        /// Cancel (payload.Cancel) is checked between steps and inside the slope step; a cancelled
        /// run returns a failed result, so the caller rolls everything back.
        /// </summary>
        public static RunResult Execute(UIApplication app, RunPayload payload)
        {
            var log = payload.Log ?? (_ => { });
            var result = new RunResult();
            try
            {
                return ExecuteSteps(app, payload, result);
            }
            catch (OperationCanceledException)
            {
                result.WasCancelled = true;
                return Fail(result, log, "Cancelled — nothing was changed.");
            }
        }

        private static RunResult ExecuteSteps(UIApplication app, RunPayload payload, RunResult result)
        {
            var log = payload.Log ?? (_ => { });
            var report = payload.Progress ?? ((_, __) => { });
            // Every progress tick is also a cancel point: it pumps the window and polls Esc.
            Action<double, string> progress = (pct, text) =>
            {
                report(pct, text);
                ToolCancellation.CheckCurrent();
            };
            CancellationToken cancel = payload.Cancel;

            Floor floor = ValidateFloor(app, payload.Document, payload.FloorId, out string error);
            if (floor == null) return Fail(result, log, error);
            Document doc = floor.Document;
            PipelineSettings settings = payload.Settings;

            // ── Optional: clear every shape point first ──────────────────────
            if (settings.ClearExistingFirst)
            {
                progress(2, "Clearing existing shape points");
                int countBefore = FloorReader.ReadShapePoints(floor).Count;
                using (var tx = new Transaction(doc, "Floor Points & Slope – clear shape points"))
                {
                    tx.Start();
                    SlabShapeEditor editor = floor.GetSlabShapeEditor();
                    if (editor != null && editor.IsEnabled) editor.ResetSlabShape();
                    if (tx.Commit() != TransactionStatus.Committed)
                        return Fail(result, log, "Clearing the existing shape points failed; nothing was changed.");
                }

                log(new LogEntry(LogLevel.Info, $"Cleared the floor's shape points and split lines ({countBefore} point(s) before)."));
                settings = settings.Clone();
                settings.ClearExistingFirst = false; // the floor now really has only its corners
            }

            ToolCancellation.CheckCurrent();
            cancel.ThrowIfCancellationRequested();

            // ── Steps 1–4 ────────────────────────────────────────────────────
            payload.Outline.ExistingVertices = FloorReader.ReadShapePoints(floor);
            PipelinePlan plan = PointPipelinePlanner.Plan(payload.Outline, settings);
            List<P2> before = payload.Outline.ExistingVertices.ToList();
            var tags = new List<(P2 Position, string Step)>(); // which step each new point came from (report)

            int stepNo = 0;
            foreach (StepPlan step in plan.Steps)
            {
                stepNo++;
                ToolCancellation.CheckCurrent();
            cancel.ThrowIfCancellationRequested();
                if (step.TooClose > 0)
                    log(new LogEntry(LogLevel.Info,
                        $"Step {stepNo} · {StepNames[step.Step]}: {step.TooClose} point(s) skipped, closer than {settings.MinPointSpacingMm:0} mm to another point."));
                if (step.Points.Count == 0) continue;

                string name = StepNames[step.Step];
                progress(stepNo * 15, $"Step {stepNo}: {name}");
                ShapePointWriteResult w = ShapePointWriter.Write(doc, floor, step.Points, $"Floor Points & Slope – {name}");
                if (!w.Success)
                    return Fail(result, log, $"Step {stepNo} ({name}) failed: {w.ErrorMessage}");

                tags.AddRange(step.Points.Select(p => (p.Position, name)));
                result.AddedPerStep[name] = w.Added;
                result.TotalAdded += w.Added;
                result.TotalRejected += w.Rejected;

                string nudged = w.Nudged > 0 ? $", {w.Nudged} after a small nudge into the floor" : string.Empty;
                string there = w.AlreadyThere > 0 ? $", {w.AlreadyThere} already there" : string.Empty;
                log(new LogEntry(LogLevel.Success, $"Step {stepNo} · {name}: {w.Added} point(s) added{nudged}{there}."));
                if (w.Rejected > 0)
                    log(new LogEntry(LogLevel.Warning,
                        $"Step {stepNo} · {name}: Revit rejected {w.Rejected} point(s) — usually a point outside the floor's top surface."));
            }

            bool anyPointStep = settings.InnerPointsEnabled || settings.PerpendicularEnabled ||
                                settings.MidQuarterEnabled || settings.GapFillEnabled;
            if (anyPointStep && result.TotalAdded == 0)
                log(new LogEntry(LogLevel.Info, "Point steps: nothing new to add — every planned point is already there."));

            // ── Step 5: ridge ────────────────────────────────────────────────
            ToolCancellation.CheckCurrent();
            cancel.ThrowIfCancellationRequested();
            if (settings.RidgeEnabled)
            {
                progress(62, "Step 5: Ridge");
                if (!RunRidge(app, doc, floor, payload, settings, result, log, tags))
                    return result;
            }

            // ── Step 6: slope ────────────────────────────────────────────────
            ToolCancellation.CheckCurrent();
            cancel.ThrowIfCancellationRequested();
            if (settings.SlopeEnabled)
            {
                progress(70, "Step 6: Slope by drain");
                var drains = payload.SelectedDrains ?? new List<DrainItem>();
                if (drains.Count == 0)
                    return Fail(result, log, "Step 6 (Slope by drain): no drain is ticked. Tick at least one opening as a drain, or turn step 6 off.");

                log(new LogEntry(LogLevel.Info, $"Step 6 · Slope by drain: {drains.Count} drain(s), {settings.SlopePercent} % (Floor Slope By Drain V001 engine)."));
                var slopePayload = new AutoSlopeDrainPayload
                {
                    FloorId = floor.Id,
                    FloorName = floor.Name,
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
                    CancelToken = cancel,
                    Log = entry => log(new LogEntry(entry.Level, "Step 6 · " + entry.Message)),
                    Progress = info => progress(70 + 0.25 * (info?.PercentWithinPhase ?? 50), "Step 6: " + info?.PhaseLabel)
                };

                AutoSlopeDrainResult slope = AutoSlopeDrainEngine.Execute(app, slopePayload);
                if (slope.WasCancelled) throw new OperationCanceledException();
                if (!slope.Success)
                    return Fail(result, log, "Step 6 (Slope by drain) failed: " + (slope.ErrorMessage ?? "see the log above."));

                result.Sloped = true;
                result.HighestElevationMm = slope.HighestElevation_mm;

                // A point the slope can't reach stays at 0 and becomes a dip in the floor.
                if (slope.UnreachableVertexCount + slope.OverThresholdVertexCount > 0)
                    log(new LogEntry(LogLevel.Warning,
                        $"Step 6 · {slope.UnreachableVertexCount + slope.OverThresholdVertexCount} point(s) were left at 0 and will show as dips: " +
                        $"{slope.UnreachableVertexCount} with no path to a drain, {slope.OverThresholdVertexCount} farther than the max path " +
                        $"({settings.MaxPathMeters} m). Raise Max path / Max edge, or tick another drain, and run again."));
            }

            result.VerticesAfter = FloorReader.ReadShapePoints(floor);
            if (settings.SaveRunReport)
                result.ReportPoints = BuildReportPoints(FloorReader.ReadShapeVertices(floor), before, tags);
            result.Success = true;
            progress(100, "Done");
            return result;
        }

        /// <summary>Step 5. Returns false (with the result marked failed) when the ridge can't be made.</summary>
        private static bool RunRidge(UIApplication app, Document doc, Floor floor, RunPayload payload,
                                     PipelineSettings settings, RunResult result, Action<LogEntry> log,
                                     List<(P2 Position, string Step)> tags)
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

            var creation = new FloorRidgeCreationService
            {
                OnSlabWarning = message => log(new LogEntry(LogLevel.Info, "Step 5 · " + message))
            };
            ElementId styleId = FloorRidgeCreationService.GetLineStyleIdByName(doc, settings.RidgeLineStyle);
            creation.CreateAll(doc, view, floor, ridge, lines, settings.RidgeShapePoints, styleId);

            if (settings.RidgeShapePoints)
                tags.AddRange(ridge.ShapePoints.Concat(ridge.RidgeEdgeMidPoints).Select(x => (new P2(x.X, x.Y), "Ridge")));
            result.RidgeLines = ridge.CreatedDetailLineIds.Count;
            result.TotalAdded += creation.TotalAdded;
            result.AddedPerStep["Ridge"] = creation.TotalAdded;
            log(new LogEntry(LogLevel.Success,
                $"Step 5 · Ridge: {ridge.ClippedEdges.Count} ridge line(s) between {ridge.DrainGroups.Count} drain group(s)" +
                (settings.RidgeShapePoints ? $", {creation.TotalAdded} point(s) added ({creation.AlreadyThere} already there) with split lines along the ridges" : string.Empty) +
                (lines ? $", {ridge.CreatedDetailLineIds.Count} detail line(s) drawn" : string.Empty) + "."));
            if (creation.TotalFailed > 0)
                log(new LogEntry(LogLevel.Warning, $"Step 5 · Ridge: {creation.TotalFailed} point(s) could not be added."));
            if (ridge.FailCount > 0)
                log(new LogEntry(LogLevel.Warning,
                    $"Step 5 · Ridge: {ridge.FailCount} of {ridge.ValidationLog.Count} ridge point(s) are not equidistant from their drains within {settings.RidgeValidationToleranceMm:0} mm."));
            return true;
        }

        /// <summary>
        /// One report row per shape point: "Existing" when it was there before the steps, else the
        /// step whose planned point is nearest (within 65 mm, as points may be nudged), else "Slope step".
        /// </summary>
        private static List<ReportPoint> BuildReportPoints(List<(P2 Position, double Z)> vertices, List<P2> before,
                                                          List<(P2 Position, string Step)> tags)
        {
            const double nudgeReach = 65.0 / 304.8;
            double minZ = vertices.Count > 0 ? vertices.Min(v => v.Z) : 0;
            var rows = new List<ReportPoint>(vertices.Count);
            foreach (var (pos, z) in vertices)
            {
                string step = null;
                if (before.Any(b => b.DistanceTo(pos) <= PointPipelinePlanner.Tolerance))
                {
                    step = "Existing";
                }
                else
                {
                    double best = double.MaxValue;
                    foreach (var (tagPos, tagStep) in tags)
                    {
                        double d = tagPos.DistanceTo(pos);
                        if (d <= nudgeReach && d < best) { best = d; step = tagStep; }
                    }
                }

                step ??= "Slope step";

                rows.Add(new ReportPoint
                {
                    Step = step,
                    XMeters = pos.X * 0.3048,
                    YMeters = pos.Y * 0.3048,
                    HeightMm = (z - minZ) * 304.8
                });
            }

            return rows.OrderBy(r => r.Step).ThenBy(r => r.XMeters).ThenBy(r => r.YMeters).ToList();
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
