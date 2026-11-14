using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Report;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>
    /// <see cref="IWetAreaSession"/> backed by an ExternalEvent. Create it inside Revit's API
    /// context (the command): it reads the model straight away.
    /// </summary>
    public sealed class RevitWetAreaSession : IWetAreaSession
    {
        private readonly Document _doc;
        private readonly WetAreaHandler _handler = new();
        private readonly ExternalEvent _externalEvent;

        /// <summary>Reads the model of <paramref name="uidoc"/> with <paramref name="settings"/>.</summary>
        public RevitWetAreaSession(UIDocument uidoc, WetAreaSettings settings)
        {
            _doc = uidoc?.Document ?? throw new ArgumentNullException(nameof(uidoc));
            Initial = RoomCollector.Read(_doc, settings);
            _externalEvent = ExternalEvent.Create(_handler);
        }

        /// <inheritdoc/>
        public ModelSnapshot Initial { get; }

        /// <inheritdoc/>
        public void Refresh(WetAreaSettings settings, Action<ModelSnapshot, string> done)
            => Raise(() => done(null, "Cancelled."), _ =>
            {
                if (!DocumentUsable(out string problem)) { done(null, problem); return; }
                done(RoomCollector.Read(_doc, settings), null);
            }, ex => done(null, ex.Message));

        /// <inheritdoc/>
        public void ListRuns(Action<IReadOnlyList<RunInfo>, string> done)
            => Raise(() => done(null, "Cancelled."), _ =>
            {
                if (!DocumentUsable(out string problem)) { done(null, problem); return; }
                done(RunRemover.List(_doc), null);
            }, ex => done(null, ex.Message));

        /// <inheritdoc/>
        public void RemoveRun(string runId, Action<int, int, string> done)
            => Raise(() => done(0, 0, "Cancelled. Nothing was removed."), _ =>
            {
                if (!DocumentUsable(out string problem)) { done(0, 0, problem); return; }
                (int deleted, int owned) = RunRemover.Remove(_doc, runId);
                done(deleted, owned, null);
            }, ex => done(0, 0, ex.Message));

        /// <inheritdoc/>
        public void Run(IReadOnlyCollection<string> roomKeys, WetAreaSettings settings, Action<RunOutcome> done)
            => Raise(
                () => done(new RunOutcome { Cancelled = true }),
                app => done(RunCore(app, roomKeys, settings)),
                ex => done(new RunOutcome { Error = ex.Message }));

        private RunOutcome RunCore(UIApplication app, IReadOnlyCollection<string> roomKeys, WetAreaSettings s)
        {
            if (!DocumentUsable(out string problem)) return new RunOutcome { Error = problem };
            if (_doc.IsReadOnly) return new RunOutcome { Error = "The document is read-only." };
            if (_doc.IsModifiable) return new RunOutcome { Error = "Finish the current Revit command first, then run again." };

            var watch = Stopwatch.StartNew();
            DateTime started = DateTime.Now;
            string runId = RunTag.NewRunId(started);

            RoofType roofType = new FilteredElementCollector(_doc).OfClass(typeof(RoofType)).Cast<RoofType>()
                .OrderBy(t => t.Name == s.RoofTypeName ? 0 : 1).ThenBy(t => t.Name).FirstOrDefault();
            if (roofType == null) return new RunOutcome { Error = "The project has no roof type. Load or create one first." };
            if (!string.IsNullOrEmpty(s.RoofTypeName) && roofType.Name != s.RoofTypeName)
                return new RunOutcome { Error = $"Roof type \"{s.RoofTypeName}\" is not in the project any more. Pick another." };

            WorksetId worksetId = null;
            if (_doc.IsWorkshared && !string.IsNullOrEmpty(s.WorksetName))
            {
                Workset workset = new FilteredWorksetCollector(_doc).OfKind(WorksetKind.UserWorkset)
                    .FirstOrDefault(w => w.Name == s.WorksetName);
                if (workset == null) return new RunOutcome { Error = $"Workset \"{s.WorksetName}\" is not in the project any more. Pick another." };
                worksetId = workset.Id;
            }

            // Read again: the model may have changed since the grid was filled.
            ModelSnapshot before = RoomCollector.Read(_doc, s);
            var keys = new HashSet<string>(roomKeys ?? Array.Empty<string>());
            var ticked = before.Rooms.Where(r => keys.Contains(r.Row.RoomKey)).ToList();
            PlannedRun plan = RunPlanner.Plan(ticked, s, runId);
            var issues = plan.Issues.ToList();
            foreach (string missing in keys.Except(ticked.Select(t => t.Row.RoomKey)))
                issues.Add(new Issue("Error", "Roof", missing, string.Empty, "The room is no longer in the model: skipped.", "Refresh the list."));

            var created = new Dictionary<string, CreatedRoof>();
            using (var group = new TransactionGroup(_doc, "Wet Area Waterproofing"))
            {
                group.Start();
                try
                {
                    for (int i = 0; i < plan.Jobs.Count; i++)
                    {
                        RoofJob job = plan.Jobs[i];
                        ToolCancellation.CheckCurrent();
                        ToolCancellation.ReportCurrent(i, plan.Jobs.Count, $"Creating roof {i + 1} of {plan.Jobs.Count} — room {job.Number}");
                        try
                        {
                            (long roofId, double areaM2) = RoofCreator.Create(_doc, job, roofType, worksetId, s);
                            created[job.RoomKey] = new CreatedRoof(roofId, areaM2, job.ReplaceRoofIds.Count > 0);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            // One room failing must not lose the others; its transaction is already rolled back.
                            issues.Add(new Issue("Error", "Roof", job.RoomKey, job.Number,
                                $"The roof could not be created: {ex.Message}", "Check the room's outline and the roof type."));
                        }
                    }

                    if (created.Count > 0) group.Assimilate();
                    else group.RollBack();
                }
                catch
                {
                    if (group.HasStarted() && !group.HasEnded()) group.RollBack(); // cancel or unexpected error: nothing is kept
                    throw;
                }
            }

            var summary = new ReportSummary
            {
                RunId = runId,
                Started = started,
                User = app.Application.Username,
                Model = before.ModelTitle,
                PluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty,
                ToolVersion = ToolCatalog.WetAreaWaterproofing.Version,
                DurationSeconds = watch.Elapsed.TotalSeconds
            };
            RunReport report = RunReport.Build(summary, before.Rooms, keys, plan, created, issues, s);

            string reportPath = null, reportError = null;
            if (s.AutoSaveReport)
            {
                try { reportPath = ReportWriter.Write(report, s.ReportFolder); }
                catch (Exception ex)
                {
                    // The roofs are already in the model; a report that cannot be saved is reported, not fatal.
                    reportError = ex.Message;
                }
            }

            return new RunOutcome
            {
                RunId = runId,
                Created = created.Values.Count(r => !r.Replaced),
                Replaced = created.Values.Count(r => r.Replaced),
                Skipped = keys.Count - created.Count,
                Issues = issues,
                ReportPath = reportPath,
                ReportError = reportError,
                Report = report,
                Snapshot = RoomCollector.Read(_doc, s)
            };
        }

        private bool DocumentUsable(out string problem)
        {
            problem = _doc == null || !_doc.IsValidObject ? "The document was closed. Close this window and start the tool again." : null;
            return problem == null;
        }

        /// <summary>Queues <paramref name="work"/> for Revit's thread; errors and cancels are answered through the callbacks.</summary>
        private void Raise(Action onCancelled, Action<UIApplication> work, Action<Exception> onError)
        {
            _handler.Queue(app =>
            {
                try { work(app); }
                catch (OperationCanceledException) { throw; }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    ToolGuard.Report(typeof(RevitWetAreaSession), ex);
                    onError(ex);
                }
            }, onCancelled);
            _externalEvent.Raise();
        }

        /// <summary>Releases the ExternalEvent.</summary>
        public void Dispose() => _externalEvent.Dispose();
    }
}
