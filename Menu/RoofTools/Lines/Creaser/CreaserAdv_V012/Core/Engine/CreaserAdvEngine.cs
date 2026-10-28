// =======================================================
// File: CreaserAdvEngine.cs
// Location: Core/Engine/
// Pipeline extracted from CreaserAdvViewModel.Run() so it can run inside
// CreaserAdvHandler (IExternalEventHandler) instead of directly from a
// button click. A modeless window cannot call the Revit API directly from a
// button click — the roof, detail symbol, and active view are all
// re-resolved here from Ids, only ever running inside a valid API context
// (Command or ExternalEvent handler), matching the convention already used
// by InnerLoopDivider/InnerLoopsAndPerpendicular/OuterCurveDivider/
// AutoSlopeByDrain.
//
// V010 restructure:
//   • Analysis (steps 1–5) is read-only and runs with NO open transaction;
//     the transaction now wraps only the placement (step 6), so an empty
//     result no longer opens-and-rolls-back a transaction.
//   • The Transaction.Commit() status is checked — V009 reported "created N"
//     even when Revit had rejected the commit and rolled everything back.
//   • Every step reports to the UI log, and a "funnel" line shows how many
//     curves survived each stage, so a run that places nothing says why.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.CreaserAdv.V012.Core.Models;
using Revit26_Plugin_Revised.CreaserAdv.V012.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.CreaserAdv.V012.Core.Engine
{
    /// <summary>Creaser Adv pipeline: roof curves → filters → detail items in the active plan view.</summary>
    public static class CreaserAdvEngine
    {
        private const double FtToMm = 304.8;
        private const int    TotalSteps = 6;

        /// <summary>
        /// Runs one pass on <paramref name="doc"/>, which must be the active project (the caller checks
        /// it is the one the window was opened for) and must have no open transaction. Only the placement
        /// step opens a transaction.
        /// </summary>
        public static CreaserAdvResult Execute(Document doc, CreaserAdvPayload payload)
        {
            LoggingService log = payload.Log;

            // ── Resolve inputs from Ids ───────────────────────────────────────
            Element roof = doc.GetElement(payload.RoofId);
            if (roof == null)
                return Fail("The roof no longer exists in the document.");

            if (doc.GetElement(payload.SelectedDetailSymbolId) is not FamilySymbol symbol)
                return Fail("The selected detail item is no longer valid.");

            if (doc.ActiveView is not ViewPlan planView)
                return Fail("Run this command from a Plan View.");

            if (planView.GenLevel == null)
                return Fail($"View '{planView.Name}' has no associated level, so lines cannot be projected into it. Use a floor/roof plan.");

            log.Section("Run started");
            LogInputs(log, doc, roof, symbol, planView, payload);

            var funnel = new Funnel();

            // ── 1. Extract crease (+ optional boundary) curves ────────────────
            log.Section($"Step 1/{TotalSteps} — Roof curves");
            RoofCurveSet curveSet = new RoofSharedTopFaceCreaseService(log)
                .Extract(roof, payload.IncludeBoundaryLines);

            IList<Curve> creaseCurves   = curveSet.Creases;
            IList<Curve> boundaryCurves = curveSet.Boundary;
            funnel.Add("creases", creaseCurves.Count);
            if (payload.IncludeBoundaryLines) funnel.Add("boundary", boundaryCurves.Count);

            // V009 gave up here whenever there were no creases, even with boundary
            // lines ticked — so a single-plane roof placed nothing. Boundary-only runs
            // are now allowed to continue.
            if (creaseCurves.Count == 0 && boundaryCurves.Count == 0)
                return NothingToPlace(log, funnel, "No crease edges found on this roof.",
                    "Creases are the edges where two sloped top faces meet; a roof with a single plane has none. " +
                    (payload.IncludeBoundaryLines
                        ? "No perimeter edges were found either — check the roof is visible in this view."
                        : "Tick 'Include boundary lines' to place items along the perimeter instead."));

            if (creaseCurves.Count == 0)
                log.Warning("No crease edges on this roof — continuing with boundary lines only.");

            // ── 1b. Drop horizontal creases (always) ──────────────────────────
            log.Section($"Step 2/{TotalSteps} — Horizontal crease filter");
            creaseCurves = new HorizontalCreaseFilterService(log).FilterOutHorizontalCreases(creaseCurves);
            funnel.Add("non-horizontal", creaseCurves.Count);

            if (creaseCurves.Count == 0 && boundaryCurves.Count == 0)
                return NothingToPlace(log, funnel, "Every crease is horizontal (both ends at the same elevation).",
                    "There is no fall along any crease, so there is no drainage direction to mark.");

            // ── 2. Dijkstra path validity (optional) ──────────────────────────
            int ridgePointCount = 0;
            int disconnectedPointCount = 0;

            if (payload.EnableDijkstraPathFilter)
            {
                log.Section($"Step 3/{TotalSteps} — Drain path (Dijkstra) filter");
                var dijkstraResult = new DijkstraPathValidityService(log).FilterByPathValidity(
                    creaseCurves, boundaryCurves, payload.MinimumSlopePercent, payload.EnableMinimumSlope);

                creaseCurves   = dijkstraResult.Creases;
                boundaryCurves = dijkstraResult.Boundary;
                ridgePointCount        = dijkstraResult.RidgePoints;
                disconnectedPointCount = dijkstraResult.DisconnectedPoints;
                funnel.Add("drain-path valid", creaseCurves.Count + boundaryCurves.Count);

                if (creaseCurves.Count == 0 && boundaryCurves.Count == 0)
                    return NothingToPlace(log, funnel, "All curves failed the Dijkstra drain-path check.",
                        "Check the slope figures above: if the roof's steepest edge is below the Minimum Slope, " +
                        "lower it or untick 'Enforce minimum slope'. You can also untick the Dijkstra filter entirely.",
                        ridgePointCount, disconnectedPointCount);
            }
            else
            {
                log.Info($"Step 3/{TotalSteps} — Dijkstra filter is off; skipped.");
            }

            // ── 3. Project to the plan view ───────────────────────────────────
            log.Section($"Step 4/{TotalSteps} — Project to plan view '{planView.Name}'");
            (creaseCurves, IList<Line> creaseLines2d) = ProjectToPlanView(doc, log, creaseCurves, planView, "crease");

            IList<Line> boundaryLines2d;
            if (payload.IncludeBoundaryLines)
                (boundaryCurves, boundaryLines2d) = ProjectToPlanView(doc, log, boundaryCurves, planView, "boundary");
            else
                boundaryLines2d = new List<Line>();

            funnel.Add("projected", creaseLines2d.Count + boundaryLines2d.Count);

            if (creaseLines2d.Count == 0 && boundaryLines2d.Count == 0)
                return NothingToPlace(log, funnel, "All lines collapsed to zero length when projected to the plan view.",
                    "The remaining creases are vertical, so they have no plan-view footprint.",
                    ridgePointCount, disconnectedPointCount);

            // ── 4. Minimum length (optional, crease only) ─────────────────────
            if (payload.EnableMinimumLength)
            {
                log.Section($"Step 5a/{TotalSteps} — Minimum length filter");
                (creaseCurves, creaseLines2d) = new MinimumLengthFilterService(log)
                    .FilterByMinimumLength(creaseCurves, creaseLines2d, payload.MinimumLengthMm);
                funnel.Add("long enough", creaseLines2d.Count + boundaryLines2d.Count);

                if (creaseLines2d.Count == 0 && boundaryLines2d.Count == 0)
                    return NothingToPlace(log, funnel,
                        $"Every crease is shorter than the {payload.MinimumLengthMm:F0} mm minimum length.",
                        "Lower the minimum length or untick 'Drop lines less than minimum'.",
                        ridgePointCount, disconnectedPointCount);
            }

            // ── 5. Drain proximity grouping (optional, crease only) ───────────
            if (payload.EnableDrainGrouping && creaseLines2d.Count > 0)
            {
                log.Section($"Step 5b/{TotalSteps} — Drain proximity grouping ({payload.DrainGroupingRadiusMm:F0} mm)");
                double radiusFt = payload.DrainGroupingRadiusMm / FtToMm;

                creaseLines2d = new DrainPointGroupingService(log)
                    .FilterByDrainProximity(creaseCurves, creaseLines2d, radiusFt);
                funnel.Add("after grouping", creaseLines2d.Count + boundaryLines2d.Count);

                if (creaseLines2d.Count == 0 && boundaryLines2d.Count == 0)
                    return NothingToPlace(log, funnel, "Drain grouping removed every crease line.",
                        "Untick 'Drain grouping radius' to place all remaining creases.",
                        ridgePointCount, disconnectedPointCount);
            }

            // ── 6. Place detail items (the only step that edits the model) ────
            log.Section($"Step 6/{TotalSteps} — Place detail items");
            var allLines = creaseLines2d.Concat(boundaryLines2d).ToList();
            funnel.Add("to place", allLines.Count);

            int placed, failed;
            using (var tx = new Transaction(doc, "Creaser Advanced V010 – Place Detail Items"))
            {
                tx.Start();

                (placed, failed) = new DetailItemPlacementService(doc, planView)
                    .PlaceAlongLines(allLines, symbol, log);

                if (placed == 0)
                {
                    tx.RollBack();
                    log.Warning($"Funnel: {funnel}");
                    log.Warning("Nothing was placed, so the transaction was rolled back.");
                    return Summary(creaseCurves.Count, boundaryCurves.Count, 0, failed, ridgePointCount, disconnectedPointCount);
                }

                TransactionStatus status = tx.Commit();
                if (status != TransactionStatus.Committed)
                {
                    log.Error($"Revit did not commit the placement (transaction status: {status}). " +
                              $"All {placed} placed item(s) were rolled back.");
                    return new CreaserAdvResult
                    {
                        Success = false,
                        ErrorMessage = $"Revit did not commit the transaction ({status})."
                    };
                }
            }

            // ── Summary ───────────────────────────────────────────────────────
            log.Info($"Funnel: {funnel}");
            if (failed == 0)
                log.Success($"Run complete — created: {placed}  failed: {failed}");
            else
                log.Warning($"Run complete — created: {placed}  failed: {failed}  (see the placement failures above)");

            return Summary(creaseCurves.Count, boundaryCurves.Count, placed, failed, ridgePointCount, disconnectedPointCount);
        }

        // --------------------------------------------------
        // Logging helpers
        // --------------------------------------------------

        private static void LogInputs(LoggingService log, Document doc, Element roof, FamilySymbol symbol, ViewPlan view, CreaserAdvPayload p)
        {
            string levelName = view.GenLevel != null ? $"{view.GenLevel.Name} @ {view.GenLevel.Elevation * FtToMm:F0} mm" : "no level";

            log.Info($"Project: {doc.Title}");
            log.Info($"View: {view.Name} ({levelName})");
            log.Info($"Roof: {roof.Name} (id {roof.Id.Value}, {roof.GetType().Name})");
            log.Info($"Detail item: {symbol.FamilyName} : {symbol.Name} (id {symbol.Id.Value})");
            log.Info($"Options — boundary lines: {OnOff(p.IncludeBoundaryLines)}, Dijkstra filter: {OnOff(p.EnableDijkstraPathFilter)}, " +
                     $"min slope: {(p.EnableDijkstraPathFilter && p.EnableMinimumSlope ? p.MinimumSlopePercent.ToString("F2") + "%" : "off")}, " +
                     $"min length: {(p.EnableMinimumLength ? p.MinimumLengthMm.ToString("F0") + " mm" : "off")}, " +
                     $"drain grouping: {(p.EnableDrainGrouping ? p.DrainGroupingRadiusMm.ToString("F0") + " mm" : "off")}");
        }

        private static string OnOff(bool value) => value ? "on" : "off";

        /// <summary>
        /// Early exit when a stage leaves nothing to place. Logs the funnel and a
        /// plain-language reason/hint, and returns a successful (but empty) result
        /// so a "Run All" sequence carries on.
        /// </summary>
        private static CreaserAdvResult NothingToPlace(
            LoggingService log, Funnel funnel, string reason, string hint,
            int ridgePoints = 0, int disconnectedPoints = 0)
        {
            log.Warning($"Funnel: {funnel}");
            log.Warning($"Nothing to place — {reason}");
            log.Info($"Hint: {hint}");
            return Summary(funnel.Count("creases"), funnel.Count("boundary"), 0, 0, ridgePoints, disconnectedPoints);
        }

        private static CreaserAdvResult Fail(string message)
            => new CreaserAdvResult { Success = false, ErrorMessage = message };

        // --------------------------------------------------
        // Projection
        // --------------------------------------------------

        private static (IList<Curve> Curves3d, IList<Line> Lines2d) ProjectToPlanView(
            Document doc,
            LoggingService log,
            IList<Curve> curves,
            ViewPlan view,
            string label)
        {
            double viewZ = view.GenLevel.Elevation;
            double tol = doc.Application.ShortCurveTolerance;
            var keptCurves = new List<Curve>();
            var keptLines = new List<Line>();
            int skipped = 0;

            foreach (Curve curve in curves)
            {
                XYZ a = curve.GetEndPoint(0);
                XYZ b = curve.GetEndPoint(1);

                XYZ p1 = new XYZ(a.X, a.Y, viewZ);
                XYZ p2 = new XYZ(b.X, b.Y, viewZ);

                if (p1.DistanceTo(p2) < tol) { skipped++; continue; }

                keptCurves.Add(curve);
                keptLines.Add(Line.CreateBound(p1, p2));
            }

            if (skipped > 0)
                log.Warning($"{skipped} {label} curve(s) projected to zero length — skipped.");

            log.Info($"{char.ToUpper(label[0])}{label.Substring(1)} lines projected: {keptLines.Count} of {curves.Count}");
            return (keptCurves, keptLines);
        }

        private static CreaserAdvResult Summary(int creasesFound, int boundaryFound, int created, int failed, int ridgePoints = 0, int disconnectedPoints = 0)
            => new CreaserAdvResult
            {
                Success = true,
                CreasesFound = creasesFound,
                BoundaryFound = boundaryFound,
                Created = created,
                Failed = failed,
                RidgePoints = ridgePoints,
                DisconnectedPoints = disconnectedPoints
            };

        // --------------------------------------------------
        // Funnel — "how many survived each stage", for the log
        // --------------------------------------------------

        private sealed class Funnel
        {
            private readonly List<(string Label, int Count)> _stages = new List<(string, int)>();

            public void Add(string label, int count) => _stages.Add((label, count));

            /// <summary>Count recorded for the first stage with this label (0 if that stage never ran).</summary>
            public int Count(string label)
            {
                foreach (var (l, c) in _stages)
                    if (l == label) return c;
                return 0;
            }

            public override string ToString()
                => string.Join(" → ", _stages.Select(s => $"{s.Count} {s.Label}"));
        }
    }
}
