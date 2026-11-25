using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Report;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Review;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Run;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Infrastructure
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
        public void Run(IReadOnlyCollection<string> roomKeys, WetAreaSettings settings, ISet<string> excluded, Action<RunOutcome> done)
            => Raise(
                () => done(new RunOutcome { Cancelled = true }),
                app => done(RunCore(app, roomKeys, settings, excluded ?? new HashSet<string>())),
                ex => done(new RunOutcome { Error = ex.Message }));

        /// <inheritdoc/>
        public void Preview(IReadOnlyCollection<string> roomKeys, WetAreaSettings settings, Action<IReadOnlyList<ReviewItem>, string> done)
            => Raise(() => done(null, "Cancelled."), _ =>
            {
                if (!DocumentUsable(out string problem)) { done(null, problem); return; }
                var keys = new HashSet<string>(roomKeys ?? Array.Empty<string>());
                ModelSnapshot snapshot = RoomCollector.Read(_doc, settings, keys);
                // Grid order, so the table reads like the rooms list.
                var ticked = snapshot.Rooms.Where(r => keys.Contains(r.Row.RoomKey)).ToList();
                PlannedRun plan = RunPlanner.Plan(ticked, settings, "preview");
                Dictionary<string, EarlierViews> earlier = ViewCreator.ReadEarlier(_doc);
                IReadOnlyList<ReviewItem> rows = ReviewBuilder.Build(ticked, plan, settings,
                    k => earlier.TryGetValue(k, out EarlierViews e) ? e : EarlierViews.None, snapshot.DraftingViews);
                done(rows, null);
            }, ex => done(null, ex.Message));

        private RunOutcome RunCore(UIApplication app, IReadOnlyCollection<string> roomKeys, WetAreaSettings s, ISet<string> excluded)
        {
            if (!DocumentUsable(out string problem)) return new RunOutcome { Error = problem };
            if (_doc.IsReadOnly) return new RunOutcome { Error = "The document is read-only." };
            if (_doc.IsModifiable) return new RunOutcome { Error = "Finish the current Revit command first, then run again." };

            var watch = Stopwatch.StartNew();
            DateTime started = DateTime.Now;
            string runId = RunTag.NewRunId(started);

            bool doRoofs = s.CreateRoofs, doViews = s.CreatePlans || s.CreateSections;
            if (!doRoofs && !doViews) return new RunOutcome { Error = "Tick at least one run step: Roofs, Plans or Reference sections." };

            RoofType roofType = null;
            WorksetId worksetId = null;
            if (doRoofs)
            {
                roofType = RoofCreator.FootPrintTypes(_doc)
                    .OrderBy(t => t.Name == s.RoofTypeName ? 0 : 1).ThenBy(t => t.Name).FirstOrDefault();
                if (roofType == null) return new RunOutcome { Error = "The project has no roof type a footprint roof can be made from. Sloped glazing types cannot be used: load or create a basic roof type first." };
                if (!string.IsNullOrEmpty(s.RoofTypeName) && roofType.Name != s.RoofTypeName)
                    return new RunOutcome { Error = $"Roof type \"{s.RoofTypeName}\" is not in the project any more, or no footprint roof can be made from it. Press Refresh and pick another." };

                if (_doc.IsWorkshared && !string.IsNullOrEmpty(s.WorksetName))
                {
                    Workset workset = new FilteredWorksetCollector(_doc).OfKind(WorksetKind.UserWorkset)
                        .FirstOrDefault(w => w.Name == s.WorksetName);
                    if (workset == null) return new RunOutcome { Error = $"Workset \"{s.WorksetName}\" is not in the project any more. Pick another." };
                    worksetId = workset.Id;
                }
            }

            ViewCreator viewCreator = null;
            if (doViews)
            {
                viewCreator = new ViewCreator(_doc, s, runId);
                bool needsCallouts = s.CreateSections && s.Targets.Any(t => t.On && t.Mode == SectionMode.Callout);
                bool needsSectionViews = s.CreateSections && s.Targets.Any(t => t.On && t.Mode == SectionMode.Section);
                bool needsDrafting = s.CreateSections && s.Targets.Any(t => t.On && SectionModes.UsesDraftingView(t.Mode));
                string notReady = viewCreator.CheckReady(needsDrafting, needsCallouts, needsSectionViews);
                if (notReady != null) return new RunOutcome { Error = notReady };
            }

            // Read again: the model may have changed since the grid was filled.
            var keys = new HashSet<string>(roomKeys ?? Array.Empty<string>());
            ModelSnapshot before = RoomCollector.Read(_doc, s, keys);
            var ticked = before.Rooms.Where(r => keys.Contains(r.Row.RoomKey)).ToList();
            var byKey = ticked.ToDictionary(t => t.Row.RoomKey);
            PlannedRun plan = RunPlanner.Plan(ticked, s, runId);
            var issues = plan.Issues.ToList();
            if (!doRoofs) issues.RemoveAll(x => x.Step == "Slab" || (x.Step == "Roof" && x.Severity == "Info")); // roof-only notes
            foreach (string missing in keys.Except(ticked.Select(t => t.Row.RoomKey)))
                issues.Add(new Issue("Error", "Roof", missing, string.Empty, "The room is no longer in the model: skipped.", "Refresh the list."));

            var created = new Dictionary<string, CreatedRoof>();
            var views = new Dictionary<string, CreatedViews>();
            using (var group = new TransactionGroup(_doc, "Wet Area Waterproofing"))
            {
                group.Start();
                try
                {
                    for (int i = 0; i < plan.Jobs.Count; i++)
                    {
                        RoofJob job = plan.Jobs[i];
                        ToolCancellation.CheckCurrent();
                        ToolCancellation.ReportCurrent(i, plan.Jobs.Count, $"Room {i + 1} of {plan.Jobs.Count} — {job.Number} {job.Name}");

                        if (doRoofs && !excluded.Contains(ReviewKeys.Roof(job.RoomKey)))
                        {
                            try
                            {
                                (long roofId, double areaM2, int holesSkipped) = RoofCreator.Create(_doc, job, roofType, worksetId);
                                created[job.RoomKey] = new CreatedRoof(roofId, areaM2, job.ReplaceRoofIds.Count > 0);
                                if (holesSkipped > 0)
                                    issues.Add(new Issue("Warning", "Roof", job.RoomKey, job.Number,
                                        $"Revit would not cut {holesSkipped} hole(s) in the roof: the roof covers them.",
                                        "Cut the openings by hand where they matter."));
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                // One room failing must not lose the others; its transaction is already rolled back.
                                // The full exception goes to the log as well: the row below holds one line of it.
                                ToolGuard.Log(typeof(RoofCreator), ex, $"Roof for room {job.Number} {job.Name}");
                                issues.Add(new Issue("Error", "Roof", job.RoomKey, job.Number,
                                    $"The roof could not be created: {ex.Message}", "Check the room's outline and the roof type."));
                            }
                        }

                        if (doViews && !excluded.Contains(ReviewKeys.Plan(job.RoomKey)))
                        {
                            try
                            {
                                CreatedViews made = CreateViews(viewCreator, byKey[job.RoomKey], job, s, issues, excluded);
                                if (made != null) views[job.RoomKey] = made;
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                ToolGuard.Log(typeof(ViewCreator), ex, $"Views for room {job.Number} {job.Name}");
                                issues.Add(new Issue("Error", "Views", job.RoomKey, job.Number,
                                    $"The plan or its sections could not be created: {ex.Message}", "Check the plan type, the view template and the drafting view names."));
                            }
                        }
                    }

                    if (created.Count > 0 || views.Count > 0) group.Assimilate();
                    else group.RollBack();
                }
                catch
                {
                    if (group.HasStarted() && !group.HasEnded()) group.RollBack(); // cancel or unexpected error: nothing is kept
                    throw;
                }
            }

            // From here on the roofs are in the model. Nothing below may turn that into "not run":
            // a report or re-read that fails is reported as a warning beside the result.
            RunReport report = null;
            string reportPath = null, reportError = null;
            ModelSnapshot after = null;
            try
            {
                var summary = new ReportSummary
                {
                    RunId = runId,
                    Started = started,
                    User = app.Application.Username,
                    Model = before.ModelTitle,
                    PluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty,
                    ToolVersion = ToolCatalog.WetAreaWaterproofingV002.Version,
                    DurationSeconds = watch.Elapsed.TotalSeconds
                };
                report = RunReport.Build(summary, before.Rooms, keys, plan, created, issues, s, views);
                if (s.AutoSaveReport) reportPath = ReportWriter.Write(report, s.ReportFolder);
            }
            catch (Exception ex)
            {
                reportError = ex.Message;
            }

            try
            {
                after = RoomCollector.Read(_doc, s);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                issues.Add(new Issue("Warning", "Refresh", null, string.Empty,
                    "The room list could not be read again after the run: " + ex.Message, "Press Refresh."));
            }
            catch (Exception)
            {
                // Cancelled while refreshing the list: the roofs stay, the list is simply not refreshed.
                issues.Add(new Issue("Warning", "Refresh", null, string.Empty,
                    "The room list was not refreshed after the run.", "Press Refresh."));
            }

            return new RunOutcome
            {
                RunId = runId,
                Created = created.Values.Count(r => !r.Replaced),
                Replaced = created.Values.Count(r => r.Replaced),
                Skipped = keys.Count(k => !created.ContainsKey(k) && !views.ContainsKey(k)),
                PlansCreated = views.Values.Count(v => v.PlanIsNew),
                PlansReused = views.Values.Count(v => !v.PlanIsNew && v.PlanId != 0),
                SectionsPlaced = views.Values.Sum(v => v.Sections.Count),
                DraftingCreated = viewCreator?.DraftingCreated ?? 0,
                CalloutsCreated = viewCreator?.CalloutsCreated ?? 0,
                SectionViewsCreated = viewCreator?.SectionViewsCreated ?? 0,
                Issues = issues,
                ReportPath = reportPath,
                ReportError = reportError,
                Report = report,
                Snapshot = after
            };
        }

        /// <summary>
        /// Plan and sections for one room, in one transaction. Returns null when there was nothing to
        /// do for the room (sections only, and none could be planned, with no plan to reuse).
        /// </summary>
        private CreatedViews CreateViews(ViewCreator creator, RoomCandidate room, RoofJob job, WetAreaSettings s, List<Issue> issues, ISet<string> excluded)
        {
            IReadOnlyList<PlannedSection> planned = Array.Empty<PlannedSection>();
            if (s.CreateSections)
            {
                SectionPlan sectionPlan = SectionPlanner.Plan(
                    new RoomSectionInput(job.Number, job.Outer, room.Inside, room.Items),
                    s.Targets, s.OneDraftingViewForAll, s.SharedDraftingViewName);
                // Rows unticked in the Review are left out; nothing moves up to take their place.
                planned = sectionPlan.Sections.Where(p => !excluded.Contains(ReviewKeys.Section(job.RoomKey, p))).ToList();
                foreach (string skipped in sectionPlan.Skipped)
                    issues.Add(new Issue("Warning", "Sections", job.RoomKey, job.Number, skipped, string.Empty));
            }
            if (!s.CreatePlans && planned.Count == 0 && room.ExistingPlanId == 0) return null;

            using var tx = new Transaction(_doc, "WP views " + job.Number);
            var failures = new RoofFailuresPreprocessor();
            FailureHandlingOptions options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(failures);
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            tx.Start();
            try
            {
                (ViewPlan view, bool isNew) = creator.EnsurePlan(room, job, planned);
                var problems = new List<string>();
                List<CreatedSection> sections = s.CreateSections
                    ? creator.PlaceSections(view, job.RoomKey, planned, problems)
                    : new List<CreatedSection>();
                foreach (string text in problems)
                    issues.Add(new Issue("Warning", "Sections", job.RoomKey, job.Number, $"Room {job.Number}: {text}", "Check the drafting view and the plan."));

                long planId = view.Id.Value;
                string planName = view.Name;
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException(failures.Error ?? "Revit did not accept the views.");
                return new CreatedViews(planId, planName, isNew, sections);
            }
            catch
            {
                if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                throw;
            }
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

            // Without this check a refused request would leave the window waiting for ever.
            ExternalEventRequest request = _externalEvent.Raise();
            if (request != ExternalEventRequest.Accepted && request != ExternalEventRequest.Pending)
            {
                _handler.Queue(null, null);
                onError(new InvalidOperationException("Revit did not accept the request (" + request + "). Try again."));
            }
        }

        /// <summary>Releases the ExternalEvent.</summary>
        public void Dispose() => _externalEvent.Dispose();
    }
}
