// IslandRoofSplitter V002 — the only place the document is touched after the window opens.
// Commit: ONE TransactionGroup for the run, ONE Transaction per ATTEMPT (ARCHITECTURE.md §5 generalised).
// A failed attempt rolls back only itself; the group is Assimilate()d when at least one island was created.
//
// Refactor (2026-10-14): Execute is wrapped in Shared ToolGuard.RunHandler (repo-wide safety net). Every failure,
// including a cancelled re-pick and "no active document", goes back through request.Error so the window always
// un-hides and leaves its busy state — previously a re-pick with no active document left the window hidden.
//
// V002 (per Anas, 2026-10-15 — "ensure roof creation; if it fails create a floor"): each island now runs an
// attempt ladder instead of a single shot, and stops at the first attempt that produces a verified element:
//     1. roof, true arcs          (skipped when Output = Floor)
//     2. roof, arcs tessellated   (only when the island actually has arc edges)
//     3. floor, true arcs         (skipped when Output = Roof, or no floor type is available)
//     4. floor, arcs tessellated  (only when the island actually has arc edges)
// Every attempt gets its own Transaction, so a rejected roof leaves nothing behind before the floor is tried.
// "Verified" means Revit kept the element AND it has solid geometry (CreationVerifier) — a committed element
// with an empty footprint counts as a failed attempt. Every attempt's outcome is reported in
// IslandCommitResult.Attempts so the log shows why the fallback was needed.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Infrastructure.ExternalEvents
{
    public sealed class IslandRoofSplitterEventHandler : IExternalEventHandler
    {
        /// <summary>Set by the manager immediately before Raise().</summary>
        public SplitterRequest? Request { get; set; }

        public string GetName() => "Island Roof Splitter V002";

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

        /// <summary>One rung of the ladder: which element to try, and whether arcs are tessellated first.</summary>
        private readonly record struct Attempt(ElementKind Kind, bool Tessellate)
        {
            public string Label => $"{Kind}{(Tessellate ? " (tessellated)" : "")}";
        }

        private static void RunCommit(Document doc, SplitterRequest req)
        {
            var c = req.Commit ?? throw new InvalidOperationException("Commit request has no payload.");
            var results = new List<IslandCommitResult>();
            int total = c.Islands.Count;

            var level = doc.GetElement(new ElementId(c.LevelId)) as Level;
            var roofType = doc.GetElement(new ElementId(c.RoofTypeId)) as RoofType;
            var floorType = c.FloorTypeId != 0 ? doc.GetElement(new ElementId(c.FloorTypeId)) as FloorType : null;

            bool roofWanted = c.Output != OutputElement.Floor;
            bool floorWanted = c.Output != OutputElement.Roof;

            string? fatal =
                doc.GetElement(new ElementId(c.SourceRoofId)) == null
                    ? "Source roof no longer exists — re-pick it and run the preview again" :
                level == null
                    ? "Selected level no longer exists — pick the level for the new elements again" :
                roofWanted && roofType == null
                    ? "Selected roof type no longer exists — pick the type for the new roofs again" :
                c.Output == OutputElement.Floor && floorType == null
                    ? "Selected floor type no longer exists — pick the floor type again" : null;

            if (fatal != null || level == null)
            {
                foreach (var isl in c.Islands)
                {
                    var r = Fail(isl, fatal ?? "No level selected for the new elements", new List<string>());
                    results.Add(r);
                    req.IslandDone?.Invoke(r, results.Count, total);
                }
                req.CommitFinished?.Invoke(results, false);
                return;
            }

            // A missing floor type only disables the fallback; the roof attempts still run.
            bool floorPossible = floorWanted && floorType != null;
            string? floorNote = floorWanted && floorType == null
                ? "floor fallback unavailable: no floor type selected"
                : null;

            double shortTolFt = doc.Application.ShortCurveTolerance;

            using var group = new TransactionGroup(doc, "Island Roof Splitter V002");
            group.Start();

            foreach (var isl in c.Islands)
            {
                var result = CommitIsland(doc, c, isl, level, roofType, floorType, floorPossible, floorNote, shortTolFt);
                results.Add(result);
                req.IslandDone?.Invoke(result, results.Count, total);
            }

            bool any = results.Any(r => r.Status == IslandStatus.Created);
            if (any) group.Assimilate(); else group.RollBack();
            req.CommitFinished?.Invoke(results, any);
        }

        /// <summary>The ladder for one island: roof attempts, then floor attempts, per the Output option.</summary>
        private static List<Attempt> Ladder(CommitRequest c, Island isl, bool floorPossible)
        {
            bool hasArcs = HasArcs(isl);
            var list = new List<Attempt>();

            if (c.Output != OutputElement.Floor)
            {
                list.Add(new Attempt(ElementKind.Roof, false));
                if (hasArcs) list.Add(new Attempt(ElementKind.Roof, true));
            }
            if (floorPossible)
            {
                list.Add(new Attempt(ElementKind.Floor, false));
                if (hasArcs) list.Add(new Attempt(ElementKind.Floor, true));
            }
            return list;
        }

        private static bool HasArcs(Island isl)
        {
            if (isl.Outer.Edges.Any(e => e.IsArc)) return true;
            foreach (var h in isl.Holes)
                if (h.Edges.Any(e => e.IsArc)) return true;
            return false;
        }

        /// <summary>One island = one ladder; each attempt is its own Transaction and the first verified one wins.</summary>
        private static IslandCommitResult CommitIsland(Document doc, CommitRequest c, Island isl, Level level,
                                                       RoofType? roofType, FloorType? floorType, bool floorPossible,
                                                       string? floorNote, double shortTolFt)
        {
            string mark = (c.MarkPrefix ?? "") + isl.Name;
            var attempts = new List<string>();
            if (floorNote != null) attempts.Add(floorNote);

            string? unusable = ProfileBuilder.Validate(isl, shortTolFt);
            if (unusable != null) return Fail(isl, $"Footprint unusable: {unusable}", attempts);

            var ladder = Ladder(c, isl, floorPossible);
            if (ladder.Count == 0) return Fail(isl, "Nothing to try: no roof type and no floor type available", attempts);

            int failedBefore = 0;
            foreach (var a in ladder)
            {
                var collector = new FailureCollector();
                using var t = new Transaction(doc, $"Island Roof Splitter — {isl.Name} · {a.Label}");
                var opts = t.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(collector);
                opts.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(opts);

                try
                {
                    t.Start();

                    // cast on the first arm so the conditional's natural type is Element (roof vs floor)
                    Element created = a.Kind == ElementKind.Roof
                        ? (Element)IslandRoofFactory.Create(doc, isl, level, roofType!, c.BaseOffsetFt, mark,
                                                            a.Tessellate, shortTolFt)
                        : IslandFloorFactory.Create(doc, isl, level, floorType!, c.BaseOffsetFt, mark,
                                                    a.Tessellate, shortTolFt);

                    if (c.Mode == SplitMode.Sloped)
                    {
                        string? push = SlabVertexPusher.Push(doc, created, isl);
                        if (push != null)
                        {
                            attempts.Add($"{a.Label}: {push}");
                            failedBefore++;
                            t.RollBack();
                            continue;
                        }
                    }

                    string? bad = CreationVerifier.Verify(doc, created);
                    if (bad != null)
                    {
                        attempts.Add($"{a.Label}: {bad}");
                        failedBefore++;
                        t.RollBack();
                        continue;
                    }

                    long newId = created.Id.Value;
                    if (t.Commit() != TransactionStatus.Committed)
                    {
                        attempts.Add($"{a.Label}: transaction rolled back" +
                                     (collector.FirstError != null ? ": " + collector.FirstError : ""));
                        failedBefore++;
                        continue;
                    }

                    attempts.Add($"{a.Label}: created, Id {newId}");
                    return new IslandCommitResult
                    {
                        Name = isl.Name,
                        Status = IslandStatus.Created,
                        ElementId = newId,
                        Mark = mark,
                        Kind = a.Kind,
                        Attempts = attempts,
                        Reason = $"{a.Label} · {c.Mode} · Mark {mark}" +
                                 (failedBefore > 0
                                     ? $" · after {failedBefore} failed attempt{(failedBefore == 1 ? "" : "s")}"
                                     : "")
                    };
                }
                catch (Exception ex)
                {
                    attempts.Add($"{a.Label}: {ex.Message}");
                    failedBefore++;
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                }
            }

            string last = attempts.Count > 0 ? attempts[attempts.Count - 1] : "no attempt was made";
            return Fail(isl, $"All {ladder.Count} attempt(s) failed · last: {last} · transaction rolled back", attempts);
        }

        private static IslandCommitResult Fail(Island isl, string reason, List<string> attempts) =>
            new()
            {
                Name = isl.Name, Status = IslandStatus.Failed, Reason = reason,
                Kind = ElementKind.None, Attempts = attempts
            };

        /// <summary>Swallows warnings (e.g. duplicate Mark); errors roll the attempt's transaction back.</summary>
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
