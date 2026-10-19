// IslandRoofSplitter V001 — the only place the document is touched after the window opens.
// Commit: ONE TransactionGroup for the run, ONE Transaction per island (ARCHITECTURE.md §5). A failed island
// rolls back only itself; the group is Assimilate()d when at least one island was created.
//
// Refactor (2026-10-14): Execute is wrapped in Shared ToolGuard.RunHandler (repo-wide safety net). Every failure,
// including a cancelled re-pick and "no active document", goes back through request.Error so the window always
// un-hides and leaves its busy state — previously a re-pick with no active document left the window hidden.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.IslandRoofSplitter.V001.Core.Models;
using Revit26_Plugin_Revised.IslandRoofSplitter.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V001.Infrastructure.ExternalEvents
{
    public sealed class IslandRoofSplitterEventHandler : IExternalEventHandler
    {
        /// <summary>Set by the manager immediately before Raise().</summary>
        public SplitterRequest? Request { get; set; }

        public string GetName() => "Island Roof Splitter V001";

        public void Execute(UIApplication app) => ToolGuard.RunHandler(GetType(), app, Run);

        private void Run(UIApplication app)
        {
            var req = Request;
            Request = null;
            if (req == null) return;

            try
            {
                var uidoc = app.ActiveUIDocument ?? throw new InvalidOperationException("No active document.");
                switch (req.Kind)
                {
                    case SplitterRequestKind.Commit:
                        RunCommit(uidoc.Document, req);
                        break;
                    case SplitterRequestKind.Highlight:
                        IslandHighlighter.Show(uidoc, req.HighlightIds);
                        break;
                    case SplitterRequestKind.RepickRoof:
                    case SplitterRequestKind.RepickLines:
                        req.RepickFinished?.Invoke(Repick(uidoc, req));
                        break;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                req.Error?.Invoke("Cancelled.");
            }
            catch (OperationCanceledException)
            {
                req.Error?.Invoke("Cancelled.");
            }
            catch (Exception ex)
            {
                // Reported to the window (which un-hides / leaves busy state); not re-thrown, so ToolGuard
                // only sees genuinely unexpected failures from the callbacks themselves.
                req.Error?.Invoke(ex.Message);
            }
        }

        private static SplitterContext Repick(UIDocument uidoc, SplitterRequest req)
        {
            var doc = uidoc.Document;
            FootPrintRoof roof;
            List<DetailCurve> lines;
            if (req.Kind == SplitterRequestKind.RepickRoof)
            {
                roof = SelectionHelper.PickRoof(uidoc);
                lines = req.CurrentLineIds.Select(i => doc.GetElement(new ElementId(i))).OfType<DetailCurve>().ToList();
            }
            else
            {
                roof = doc.GetElement(new ElementId(req.CurrentRoofId)) as FootPrintRoof
                       ?? throw new InvalidOperationException("The source roof no longer exists. Re-pick the roof.");
                lines = SelectionHelper.PickDetailCurves(uidoc);
            }
            if (lines.Count == 0) throw new InvalidOperationException("The picked detail lines no longer exist. Re-pick the lines.");
            return ContextFactory.Build(uidoc, roof, lines);
        }

        private static void RunCommit(Document doc, SplitterRequest req)
        {
            var c = req.Commit ?? throw new InvalidOperationException("Commit request has no payload.");
            var results = new List<IslandCommitResult>();
            int total = c.Islands.Count;

            var level = doc.GetElement(new ElementId(c.LevelId)) as Level;
            var type = doc.GetElement(new ElementId(c.RoofTypeId)) as RoofType;
            string? fatal =
                doc.GetElement(new ElementId(c.SourceRoofId)) == null ? "Source roof no longer exists — re-pick it and run the preview again" :
                level == null ? "Selected level no longer exists — pick the level for the new roofs again" :
                type == null ? "Selected roof type no longer exists — pick the type for the new roofs again" : null;
            if (fatal != null || level == null || type == null)
            {
                foreach (var isl in c.Islands)
                {
                    var r = Fail(isl, fatal ?? "No level or roof type selected for the new roofs");
                    results.Add(r);
                    req.IslandDone?.Invoke(r, results.Count, total);
                }
                req.CommitFinished?.Invoke(results, false);
                return;
            }

            using var group = new TransactionGroup(doc, "Island Roof Splitter V001");
            group.Start();

            foreach (var isl in c.Islands)
            {
                var result = CommitIsland(doc, c, isl, level, type);
                results.Add(result);
                req.IslandDone?.Invoke(result, results.Count, total);
            }

            bool any = results.Any(r => r.Status == IslandStatus.Created);
            if (any) group.Assimilate(); else group.RollBack();
            req.CommitFinished?.Invoke(results, any);
        }

        /// <summary>One island = one Transaction; a failure rolls back this island only.</summary>
        private static IslandCommitResult CommitIsland(Document doc, CommitRequest c, Island isl, Level level, RoofType type)
        {
            string mark = (c.MarkPrefix ?? "") + isl.Name;
            var collector = new FailureCollector();

            using var t = new Transaction(doc, $"Island Roof Splitter — {isl.Name}");
            var opts = t.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(collector);
            opts.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(opts);

            try
            {
                t.Start();
                var roof = IslandRoofFactory.Create(doc, isl, level, type, c.BaseOffsetFt, mark);

                string? pushError = c.Mode == SplitMode.Sloped ? RoofVertexPusher.Push(doc, roof, isl) : null;
                if (pushError != null)
                {
                    t.RollBack();
                    return Fail(isl, pushError + " · transaction rolled back");
                }

                long newId = roof.Id.Value;
                return t.Commit() == TransactionStatus.Committed
                    ? new IslandCommitResult
                    {
                        Name = isl.Name, Status = IslandStatus.Created, ElementId = newId, Mark = mark,
                        Reason = $"{c.Mode} · Mark {mark}"
                    }
                    : Fail(isl, "Transaction rolled back" + (collector.FirstError != null ? ": " + collector.FirstError : ""));
            }
            catch (Exception ex)
            {
                if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                return Fail(isl, ex.Message + " · transaction rolled back");
            }
        }

        private static IslandCommitResult Fail(Island isl, string reason) =>
            new() { Name = isl.Name, Status = IslandStatus.Failed, Reason = reason };

        /// <summary>Swallows warnings (e.g. duplicate Mark); errors roll the island's transaction back.</summary>
        private sealed class FailureCollector : IFailuresPreprocessor
        {
            public string? FirstError { get; private set; }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                bool hasError = false;
                foreach (var f in fa.GetFailureMessages())
                {
                    if (f.GetSeverity() == FailureSeverity.Warning) { fa.DeleteWarning(f); continue; }
                    hasError = true;
                    FirstError ??= f.GetDescriptionText();
                }
                return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
