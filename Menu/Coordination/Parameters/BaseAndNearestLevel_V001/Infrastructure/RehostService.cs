using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;
using Revit26_Plugin_Revised.Shared.Services;
using LogLevel = Revit26_Plugin_Revised.Shared.Models.LogLevel;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure
{
    /// <summary>
    /// Re-hosts elements onto their nearest level without moving them: the level parameter is set first and
    /// the offset second, so the element's base elevation is unchanged. A wall's bottom stays put, and its
    /// top stays put whether Top Constraint is a level or Unconnected.
    /// <para>
    /// Everything happens inside one <see cref="TransactionGroup"/> that is assimilated into a single undo
    /// step, so one Ctrl+Z puts the model back. The four target parameters are refreshed in the same group
    /// once the elements have moved, so they never describe the old placement.
    /// </para>
    /// </summary>
    public static class RehostService
    {
        private const string GroupName = "Re-host to Nearest Level";

        /// <summary>
        /// Re-hosts every ticked element whose nearest level is not its base level and that nothing blocks.
        /// Elements that are shape-edited, grouped, read-only or owned by another user are skipped and
        /// reported; <see cref="RunRequest.RehostShapeEdited"/> includes the shape-edited ones.
        /// </summary>
        public static RunResult Run(Document doc, RunRequest request)
        {
            var run = new RunResult();
            FactsResult facts = GatherAndCheck(doc, request, run);
            if (run.NotStartedReason != null) return run;
            run.Facts = facts;

            var plan = new List<ElementFacts>();
            foreach (ElementFacts e in facts.Elements)
            {
                RehostSkip block = BaseAndNearestRules.RehostBlockFor(e, request.RehostShapeEdited);
                if (block == RehostSkip.None) plan.Add(e);
                else if (block != RehostSkip.NoChange)
                    run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Skipped, Text = "Skipped · " + BaseAndNearestRules.RehostSkipText(block) });
            }

            if (plan.Count == 0)
            {
                run.NotStartedReason = "No element can be re-hosted; nothing was changed.";
                return run;
            }

            using var group = new TransactionGroup(doc, GroupName);
            group.Start();
            try
            {
                int moved = Move(doc, plan, run);
                if (moved == 0)
                {
                    group.RollBack();
                    run.NotStartedReason = "No element could be re-hosted; nothing was changed.";
                    return run;
                }

                Refresh(doc, request, run);

                TransactionStatus status = group.Assimilate();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException($"Revit did not keep the re-host (status {status}). Nothing was changed.");
                run.Committed = true;
                run.Log.Add((LogLevel.Success, $"Re-hosted {moved} element(s). Undo puts all of it back in one step."));
            }
            catch
            {
                if (group.HasStarted() && !group.HasEnded()) group.RollBack();
                throw;
            }
            return run;
        }

        // Writes the level, then the offset, so the elevation does not change between the two.
        private static int Move(Document doc, List<ElementFacts> plan, RunResult run)
        {
            int moved = 0;
            using var tx = new Transaction(doc, "Re-host to nearest level");
            tx.Start();

            int done = 0;
            foreach (ElementFacts e in plan)
            {
                ToolCancellation.CheckCurrent();
                ToolCancellation.ReportCurrent(++done, plan.Count, $"Re-hosting {done} of {plan.Count}…");

                Element element = doc.GetElement(new ElementId(e.Id));
                if (element == null)
                {
                    run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Skipped, Text = "Skipped · element no longer exists" });
                    continue;
                }

                // Read the pair again: the element is the authority, not the facts gathered earlier.
                BasePlacement placement = LevelPairReader.Read(doc, element);
                if (!placement.Found || placement.ReadOnly)
                {
                    run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Skipped, Text = "Skipped · its level parameter is read-only" });
                    continue;
                }

                Parameter levelParam = element.get_Parameter(placement.Pair.Level);
                Parameter offsetParam = element.get_Parameter(placement.Pair.Offset);
                if (levelParam == null || offsetParam == null)
                {
                    run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Skipped, Text = "Skipped · level or offset parameter not found" });
                    continue;
                }

                try
                {
                    if (!levelParam.Set(new ElementId(e.NearestLevelId)))
                    {
                        run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Failed, Text = $"Failed · Revit refused the level “{e.NearestLevelName}”" });
                        continue;
                    }
                    if (!offsetParam.Set(UnitsMm.ToFeet(e.NearestOffsetMm)))
                    {
                        run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Failed, Text = "Failed · Revit refused the new offset" });
                        continue;
                    }
                    moved++;
                    run.Rows.Add(new RunRowResult
                    {
                        Id = e.Id,
                        State = RowState.Assigned,
                        Text = $"Re-hosted · {e.BaseLevelName} {ValueText.Offset(e.BaseOffsetMm)} → {e.NearestLevelName} {ValueText.Offset(e.NearestOffsetMm)}"
                    });
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Failed, Text = "Failed · " + ex.Message });
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Failed, Text = "Failed · " + ex.Message });
                }
            }

            TransactionStatus status = tx.Commit();
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException($"Revit did not commit the re-host (status {status}). Nothing was changed.");
            return moved;
        }

        // Re-reads the moved elements and writes the four parameters again, inside the same undo step.
        private static void Refresh(Document doc, RunRequest request, RunResult run)
        {
            FactsResult after = TargetParameterService.GatherFacts(doc, request);
            InternalDefinition[] targets = TargetParameterService.ResolveTargets(doc, after);

            var missing = TargetSlots.All.Where(s => targets[(int)s] == null).Select(TargetSlots.Label).ToList();
            if (missing.Count > 0)
            {
                run.Log.Add((LogLevel.Warning,
                    "The four parameters were not refreshed after the re-host, because these are not in the model yet: " +
                    string.Join(", ", missing) + ". Run Write Parameters to bring them up to date."));
                run.Facts = after;
                return;
            }

            using var tx = new Transaction(doc, "Refresh level parameters");
            tx.Start();
            int refreshed = 0;
            foreach (ElementFacts e in after.Elements)
            {
                ToolCancellation.CheckCurrent();
                if (!e.HasBase || !e.NearestFound || !string.IsNullOrEmpty(e.OwnedBy)) continue;
                RunRowResult row = TargetParameterService.AssignAll(doc, e, targets);
                if (row.State == RowState.Assigned) refreshed++;
                run.ValuesWritten += row.Written;
            }
            TransactionStatus status = tx.Commit();
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException($"Revit did not commit the refreshed parameters (status {status}).");

            run.Log.Add((LogLevel.Info, $"Refreshed the four parameters on {refreshed} element(s) after the re-host."));
            run.Facts = after;
        }

        private static FactsResult GatherAndCheck(Document doc, RunRequest request, RunResult run)
        {
            FactsResult facts = TargetParameterService.GatherFacts(doc, request);
            if (facts.Document.IsReadOnly)
                run.NotStartedReason = "The document is read-only, so nothing can be re-hosted.";
            else if (facts.Document.Candidates.Count == 0)
                run.NotStartedReason = "This project has no candidate level, so no element can be re-hosted.";
            else if (facts.Elements.Count == 0)
                run.NotStartedReason = "No roof, floor or wall is ticked.";
            return facts;
        }
    }
}
