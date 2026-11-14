// =======================================================
// File: InnerLoopDividerEngine.cs
// Location: Core/Engine/
// Orchestration only — Analyze → FloorLoopReader (read-only),
// Apply → ShapePointWriter (one transaction).
// Changes vs V011: uses the payload's document (not whatever is active),
// reports exact counts, never resets offsets unless asked.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Models;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Engine
{
    public static class InnerLoopDividerEngine
    {
        public static Document ResolveDocument(UIApplication app, InnerLoopDividerPayload payload)
        {
            Document doc = payload.Document;
            if (doc != null && doc.IsValidObject) return doc;
            return payload.Document == null ? app.ActiveUIDocument?.Document : null;
        }

        public static InnerLoopDividerResult Execute(UIApplication app, InnerLoopDividerPayload payload)
        {
            var log = payload.Log ?? (_ => { });
            Document doc = ResolveDocument(app, payload);
            if (doc == null)
                return Fail(payload, log, "The document with this floor is closed. Close the tool and run it again.");

            if (doc.GetElement(payload.FloorId) is not Floor floor)
                return Fail(payload, log, "Floor element not found (it may have been deleted).");

            return payload.Operation == InnerLoopDividerOperation.Analyze
                ? ExecuteAnalyze(floor)
                : ExecuteApply(doc, floor, payload, log);
        }

        private static InnerLoopDividerResult ExecuteAnalyze(Floor floor)
        {
            FloorLoopReadResult read = FloorLoopReader.Read(floor);
            return new InnerLoopDividerResult
            {
                Success = true,
                Operation = InnerLoopDividerOperation.Analyze,
                Loops = read.InnerLoops,
                Notes = read.Notes
            };
        }

        private static InnerLoopDividerResult ExecuteApply(Document doc, Floor floor, InnerLoopDividerPayload payload, Action<LogEntry> log)
        {
            var loops = payload.SelectedLoops ?? new System.Collections.Generic.List<FloorLoopModel>();
            ShapePointWriteResult w = ShapePointWriter.Apply(doc, floor, loops, payload.Mode, payload.SpacingFeet, payload.ResetOffsetsFirst);

            if (!w.Success)
                return Fail(payload, log, "Apply failed and was rolled back: " + w.ErrorMessage);

            string reset = w.OffsetsReset > 0 ? $", {w.OffsetsReset} existing offset(s) reset to 0" : string.Empty;
            string failed = w.Failed > 0 ? $", {w.Failed} rejected by Revit" : string.Empty;
            log(new LogEntry(LogLevel.Success,
                $"Done — {w.Added} new point(s) on {w.LoopsProcessed} loop(s), {w.SkippedExisting} already existed{failed}{reset}."));

            return new InnerLoopDividerResult
            {
                Success = true,
                Operation = InnerLoopDividerOperation.Apply,
                PointsAdded = w.Added,
                PointsSkippedExisting = w.SkippedExisting,
                PointsFailed = w.Failed,
                OffsetsReset = w.OffsetsReset,
                LoopsProcessed = w.LoopsProcessed,
                VerticesAfter = w.VerticesAfter.ToList()
            };
        }

        private static InnerLoopDividerResult Fail(InnerLoopDividerPayload payload, Action<LogEntry> log, string msg)
        {
            log(new LogEntry(LogLevel.Error, msg));
            return new InnerLoopDividerResult
            {
                Success = false,
                ErrorMessage = msg,
                Operation = payload.Operation,
                PointsAdded = -1
            };
        }
    }
}
