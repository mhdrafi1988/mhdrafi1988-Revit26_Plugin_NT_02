// =======================================================
// File: InnerLoopsAndPerpendicularEngine.cs
// Location: Core/Engine/
// New in V005. Orchestration only — dispatches to RoofGeometryService
// (Analyze), PerpendicularPointService.GenerateCandidates (Generate), or
// PerpendicularPointService.ApplyPerpendicularPoints (Apply) based on
// payload.Operation.
// =======================================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.Core.Models;
using Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.Core.Engine
{
    public static class InnerLoopsAndPerpendicularEngine
    {
        public static InnerLoopsAndPerpendicularResult Execute(UIApplication app, InnerLoopsAndPerpendicularPayload payload)
        {
            var log = payload.Log ?? (_ => { });
            Document doc = app.ActiveUIDocument.Document;

            RoofBase roof = doc.GetElement(payload.RoofId) as RoofBase;
            if (roof == null)
            {
                const string msg = "Roof element not found (it may have been deleted).";
                log(new LogEntry(LogLevel.Error, msg));
                return new InnerLoopsAndPerpendicularResult
                {
                    Success = false,
                    ErrorMessage = msg,
                    Operation = payload.Operation
                };
            }

            return payload.Operation switch
            {
                InnerLoopsAndPerpendicularOperation.Analyze  => ExecuteAnalyze(roof),
                InnerLoopsAndPerpendicularOperation.Generate => ExecuteGenerate(roof, payload, log),
                InnerLoopsAndPerpendicularOperation.Apply    => ExecuteApply(doc, roof, payload, log),
                _ => new InnerLoopsAndPerpendicularResult { Success = false, ErrorMessage = "Unknown operation." }
            };
        }

        private static InnerLoopsAndPerpendicularResult ExecuteAnalyze(RoofBase roof)
        {
            var geometryService = new RoofGeometryService();
            var allLoops = geometryService.ExtractCircularLoops(roof);

            var outerLoop = allLoops.FirstOrDefault(l => l.LoopType == "Outer");
            var innerLoops = allLoops.Where(l => l.LoopType == "Inner").ToList();

            return new InnerLoopsAndPerpendicularResult
            {
                Success = true,
                Operation = InnerLoopsAndPerpendicularOperation.Analyze,
                Loops = innerLoops,
                OuterLoop = outerLoop
            };
        }

        private static InnerLoopsAndPerpendicularResult ExecuteGenerate(
            RoofBase roof, InnerLoopsAndPerpendicularPayload payload, Action<LogEntry> log)
        {
            if (payload.OuterLoop == null)
            {
                log(new LogEntry(LogLevel.Error, "Cannot generate perpendicular points — no outer boundary loop available."));
                return new InnerLoopsAndPerpendicularResult
                {
                    Success = true,
                    Operation = InnerLoopsAndPerpendicularOperation.Generate,
                    PerpendicularPoints = new List<PerpendicularPointModel>()
                };
            }

            var service = new PerpendicularPointService();
            var allPoints = new List<PerpendicularPointModel>();

            foreach (var shape in payload.SelectedLoops)
            {
                ToolCancellation.CheckCurrent();
                var sideCandidates = service.GenerateCandidates(
                    roof, shape, payload.OuterLoop, payload.ToleranceMm, payload.GroupProximityMm);

                int validCount = sideCandidates.Count(c => c.IsValid);
                if (validCount == 0)
                {
                    log(new LogEntry(LogLevel.Warning,
                        $"Shape #{shape.Index} ({shape.LoopShapeType}): no boundary candidates found — skipped."));
                    continue;
                }

                log(new LogEntry(LogLevel.Info,
                    $"Shape #{shape.Index} ({shape.LoopShapeType}): {validCount} perpendicular candidate(s) found."));

                foreach (var candidate in sideCandidates)
                {
                    if (!candidate.IsValid)
                    {
                        log(new LogEntry(LogLevel.Warning,
                            $"Shape #{candidate.ShapeIndex}: a point group had fewer than 4 directional candidates available."));
                        continue;
                    }

                    allPoints.Add(candidate);
                }
            }

            log(new LogEntry(LogLevel.Success,
                $"Generated {allPoints.Count(p => p.IsValid)} perpendicular point candidate(s)."));

            return new InnerLoopsAndPerpendicularResult
            {
                Success = true,
                Operation = InnerLoopsAndPerpendicularOperation.Generate,
                PerpendicularPoints = allPoints
            };
        }

        private static InnerLoopsAndPerpendicularResult ExecuteApply(
            Document doc, RoofBase roof, InnerLoopsAndPerpendicularPayload payload, Action<LogEntry> log)
        {
            var service = new PerpendicularPointService();
            int placed = service.ApplyPerpendicularPoints(doc, roof, payload.SelectedPoints, out var details);

            foreach (var line in details)
                log(new LogEntry(line.Contains("rejected") ? LogLevel.Warning : LogLevel.Info, line));

            if (placed >= 0)
            {
                ToolCancellation.CheckCurrent();
                log(new LogEntry(LogLevel.Success, $"Done — {placed} perpendicular point(s) added."));
                return new InnerLoopsAndPerpendicularResult
                {
                    Success = true,
                    Operation = InnerLoopsAndPerpendicularOperation.Apply,
                    PointsAdded = placed
                };
            }

            const string failMsg = "Apply failed — transaction error.";
            log(new LogEntry(LogLevel.Error, failMsg));
            return new InnerLoopsAndPerpendicularResult
            {
                Success = false,
                ErrorMessage = failMsg,
                Operation = InnerLoopsAndPerpendicularOperation.Apply,
                PointsAdded = -1
            };
        }
    }
}
