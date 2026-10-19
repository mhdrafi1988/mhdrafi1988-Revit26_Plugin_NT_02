// ==============================================
// File: WorksetService.cs
// Layer: Core/Services  (no Revit document mutation outside a Transaction;
//         no UI / ViewModel dependency — CLAUDE.md)
// Workset Creator V016.
//
// Changes vs V015:
//   FIX  Instances are found by link TYPE id, not by RevitLinkInstance
//        .GetLinkDocument().Title. GetLinkDocument() returns null for an
//        unloaded link, so V015 reported every unloaded link as "0 instances",
//        dropped it into Grid 3 and created a workset that stayed empty while
//        the link's real instances sat in Workset1. Matching on
//        RevitLinkInstance.GetTypeId() works whatever the load state.
//   NEW  ScanLinks does the whole read in one pass and returns plain data
//        (LinkScan / LinkRecord). V015 asked the document one question per link
//        per property and ran a FilteredWorksetCollector inside a per-instance
//        loop — O(links x instances x worksets) on a 59-link model.
//   NEW  IFC links are excluded (confirmed with Rafi, 2026-10-17). Revit links
//        an IFC through a generated "<file>.ifc.rvt" shadow model, so it is a
//        perfectly ordinary RevitLinkType and V015 listed it like any other.
//        Later in V016 this was replaced by a positive rule, confirmed with
//        Rafi 2026-10-18: a link is listed when the file it points at ends in
//        .rvt. An IFC that Revit linked is a "<file>.ifc.rvt" shadow model, so
//        it ends in .rvt and is ALWAYS listed and always gets a workset. A bare
//        .ifc is not a Revit file and is never listed. IsIfcLink survives only
//        to colour the row — it no longer decides anything.
//   NEW  ReadAllWorksets + RenameWorkset + a widened ResolveDuplicates, for the
//        duplicate-prevention layer. ResolveDuplicates used to be seeded from
//        Grid 1 alone, so it was blind to worksets already in the model under a
//        different naming pattern — which is how one link ended up with two.
//   NEW  Nested links are excluded — a nested link's workset follows its parent,
//        so there is nothing here to assign and V015 only ever created an empty
//        workset for one.
//   NEW  A Create whose workset name already exists re-uses it instead of
//        throwing. V015 decided CreateNew at scan time and never re-checked, so
//        editing the pattern into an existing name produced a hard Error row.
//   NEW  A link that is unloaded and assigns nothing is reported with that as
//        the reason, rather than the generic read-only/pinned note.
// Everything else — name cleaning, duplicate resolution, one Transaction per
// link, roll-back-and-continue, the run-wide TransactionGroup — is V015.
// ==============================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WorksetCreator.V016.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.WorksetCreator.V016.Core.Services
{
    public class WorksetService
    {
        private readonly Action<LogEntry> _log;

        public WorksetService(Action<LogEntry> logger) => _log = logger;

        // ─── Scan ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads every RevitLinkType in <paramref name="doc"/> once and returns the
        /// links this tool will act on, plus the ones it deliberately left out.
        /// Pure read — no transaction, nothing mutated.
        /// </summary>
        /// <remarks>
        /// The inclusion rule is by file extension, not by format: <b>a link is listed
        /// when the file it points at ends in .rvt</b>. That covers an IFC Revit linked,
        /// because Revit converts it to a "&lt;file&gt;.ifc.rvt" shadow model and links
        /// that — it is a Revit file and needs a workset like any other. A bare .ifc is
        /// not a Revit file and is never listed.
        ///
        /// When the extension cannot be determined at all the link is listed anyway and
        /// a Warning is logged: silently dropping a real link is far worse than listing
        /// an odd one.
        /// </remarks>
        public LinkScan ScanLinks(Document doc)
        {
            var scan = new LinkScan();
            if (doc == null) return scan;

            // One pass over the instances, bucketed by their type id. This is the
            // fix: no GetLinkDocument() anywhere, so load state is irrelevant.
            var instancesByType = new Dictionary<int, List<RevitLinkInstance>>();
            foreach (RevitLinkInstance instance in new FilteredElementCollector(doc)
                         .OfClass(typeof(RevitLinkInstance))
                         .Cast<RevitLinkInstance>())
            {
                int typeId = (int)(instance.GetTypeId()?.Value ?? ElementId.InvalidElementId.Value);
                if (!instancesByType.TryGetValue(typeId, out List<RevitLinkInstance> bucket))
                    instancesByType[typeId] = bucket = new List<RevitLinkInstance>();
                bucket.Add(instance);
            }

            // One pass over the worksets, so the per-instance lookup below is a
            // dictionary hit instead of a fresh collector.
            Dictionary<int, string> worksetNames = BuildWorksetNameMap(doc);

            foreach (RevitLinkType type in new FilteredElementCollector(doc)
                         .OfClass(typeof(RevitLinkType))
                         .Cast<RevitLinkType>())
            {
                // Nested links are never ours: a nested link's workset follows its
                // parent, so there is nothing here to set.
                if (IsNestedLink(type)) { scan.ExcludedNested++; continue; }

                string rawName = type.Name ?? string.Empty;
                bool   isIfc   = IsIfcLink(type);

                // The rule: ends in .rvt -> it is a Revit file and it is listed.
                // "Z3.ifc.rvt" qualifies, which is the whole point.
                LinkFileKind kind = ClassifyLinkFile(type, rawName);

                if (kind == LinkFileKind.NotRevitFile)
                {
                    // A bare .ifc (or anything else that is not a .rvt). Counted under
                    // ExcludedIfc when it is an IFC so the existing report wording still
                    // reads correctly.
                    if (isIfc) scan.ExcludedIfc++;
                    else       scan.ExcludedNonRevit++;
                    continue;
                }

                if (kind == LinkFileKind.Unknown)
                    _log(new LogEntry(LogLevel.Warning,
                        $"Could not determine the file type of link '{rawName}' — listing it anyway. " +
                        "A link is only ever left out when it is positively not a .rvt file."));

                if (isIfc) scan.IncludedIfc++;

                var record = new LinkRecord
                {
                    TypeId      = (int)type.Id.Value,

                    // Strips a trailing ".rvt" and nothing else, so "Z3.ifc.rvt" keeps its
                    // .ifc -> "Z3.ifc". Path.GetFileNameWithoutExtension would have been
                    // wrong here: on a name like "Phase V1.2" it strips ".2".
                    Name        = StripRvtExtension(rawName),

                    // What the grid shows — the name with its extension, so a double
                    // extension is visible instead of silently trimmed.
                    DisplayName = rawName,

                    IsIfc       = isIfc,
                    LoadState   = MapLoadState(type),
                    AccState    = AccLinkStatus.Read(doc, type.Id)
                };

                if (instancesByType.TryGetValue(record.TypeId, out List<RevitLinkInstance> instances))
                {
                    record.InstanceCount = instances.Count;

                    var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RevitLinkInstance instance in instances)
                    {
                        Parameter param = instance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (param == null || !param.HasValue)
                        {
                            record.UnassignedInstances++;
                            continue;
                        }

                        if (worksetNames.TryGetValue(param.AsInteger(), out string name))
                            distinct.Add(name);
                        else
                            record.UnassignedInstances++;
                    }

                    record.WorksetNames.AddRange(distinct.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                }

                scan.Links.Add(record);
            }

            scan.Links.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return scan;
        }

        public string CheckExistingWorkset(Document doc, string worksetName)
        {
            Workset existing = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(ws =>
                    ws.Name.Equals(worksetName, StringComparison.OrdinalIgnoreCase));
            return existing?.Name;
        }

        /// <summary>
        /// Every user workset in the model, flattened for the matcher: name, id and how
        /// many elements sit in it. Pure read, no transaction.
        ///
        /// The element count is taken with ElementWorksetFilter.GetElementCount(), one
        /// cheap filtered count per workset, rather than one pass over every element in
        /// the document grouped by workset — GetElementCount() never hydrates the
        /// elements, so ~150 worksets cost far less than ~500k element reads.
        ///
        /// That count is what the UI tooltip shows before a rename is accepted, so a
        /// workset holding more than this link's instances is visible rather than a
        /// surprise.
        /// </summary>
        public List<WorksetInfo> ReadAllWorksets(Document doc)
        {
            var result = new List<WorksetInfo>();
            if (doc == null) return result;

            // A non-workshared document has no worksets at all, and
            // FilteredWorksetCollector on one is not worth risking.
            if (!doc.IsWorkshared)
            {
                _log(new LogEntry(LogLevel.Warning,
                    "Document is not workshared — no worksets to read, so nothing can be matched."));
                return result;
            }

            try
            {
                foreach (Workset workset in new FilteredWorksetCollector(doc)
                             .OfKind(WorksetKind.UserWorkset))
                {
                    int elementCount;
                    try
                    {
                        elementCount = new FilteredElementCollector(doc)
                            .WherePasses(new ElementWorksetFilter(workset.Id))
                            .GetElementCount();
                    }
                    catch (Exception)
                    {
                        // A count is a nicety; never let it cost us the workset itself.
                        elementCount = -1;
                    }

                    result.Add(new WorksetInfo
                    {
                        Id           = workset.Id.IntegerValue,
                        Name         = workset.Name,
                        ElementCount = elementCount
                    });
                }

                _log(new LogEntry(LogLevel.Info, $"Read {result.Count} user workset(s) for matching."));
            }
            catch (Exception ex)
            {
                _log(new LogEntry(LogLevel.Warning,
                    $"Could not read the workset list — matching is disabled for this scan. {ex.Message}"));
            }

            return result;
        }

        // ─── Name helpers (V015, unchanged) ───────────────────────────────────

        /// <summary>
        /// Makes every proposed name unique by appending _2, _3 … where two rows would
        /// otherwise collide.
        /// </summary>
        /// <param name="existingWorksets">
        /// The model's current worksets. Seeding from these is the fix: the original
        /// version seeded from Grid 1 alone, so a workset already in the model under a
        /// different naming pattern was invisible and the tool happily proposed a name
        /// that collided with it.
        ///
        /// A row whose own matched workset carries the name is NOT bumped — that name is
        /// its rightful target, and renaming it to "X_2" would be the duplicate we are
        /// trying to prevent.
        /// </param>
        public void ResolveDuplicates(
            IEnumerable<WorksetItem> items,
            IEnumerable<WorksetInfo> existingWorksets = null)
        {
            var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Seed with Grid 1 names so they are never collided into
            foreach (var item in items.Where(i => i.GridCategory == WorksetGridCategory.AlreadyAssigned))
                usedNames[item.ProposedWorksetName] = 1;

            // Seed with the model's own worksets, EXCEPT the ones a row has matched —
            // those belong to that row and are its rename target.
            var claimedWorksetIds = new HashSet<int>(
                items.Select(i => i.SelectedTargetWorksetId != 0
                                      ? i.SelectedTargetWorksetId
                                      : i.MatchedWorksetId)
                     .Where(id => id != 0));

            foreach (WorksetInfo workset in existingWorksets ?? Enumerable.Empty<WorksetInfo>())
            {
                if (workset == null || string.IsNullOrWhiteSpace(workset.Name)) continue;
                if (claimedWorksetIds.Contains(workset.Id)) continue;
                usedNames[workset.Name] = 1;
            }

            foreach (var item in items.Where(i => i.GridCategory != WorksetGridCategory.AlreadyAssigned))
            {
                // A row that is adopting or re-using its matched workset keeps that
                // workset's name verbatim — there is nothing to de-duplicate.
                if (item.SelectedAction == WorksetAction.Adopt &&
                    (item.SelectedTargetWorksetId != 0 || item.MatchedWorksetId != 0))
                {
                    item.RefreshFinalWorksetName();
                    continue;
                }

                string baseName  = item.ProposedWorksetName;
                string finalName = baseName;
                int    count     = 1;

                while (usedNames.ContainsKey(finalName))
                {
                    count++;
                    finalName = $"{baseName}_{count}";
                }

                if (finalName != baseName)
                {
                    _log(new LogEntry(LogLevel.Warning,
                        $"Name '{baseName}' is already taken (another row, or a workset already " +
                        $"in this model) → this link will use '{finalName}' instead"));
                    item.ProposedWorksetName = finalName;
                }

                item.RefreshFinalWorksetName();
                usedNames[finalName] = count;
            }
        }

        public string CleanName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "Workset";

            string cleaned = input;

            char[] invalidChars = { ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '\t', '\n', '\r' };
            foreach (char c in invalidChars)
                cleaned = cleaned.Replace(c, '-');

            while (cleaned.Contains("--"))
                cleaned = cleaned.Replace("--", "-");

            cleaned = cleaned.Trim().Trim('-');

            if (string.IsNullOrEmpty(cleaned))
                cleaned = "Workset";

            if (cleaned.Length > 150)
            {
                string truncated = cleaned[..150];
                _log(new LogEntry(LogLevel.Warning,
                    $"Truncated '{cleaned}' to 150 chars: '{truncated}'"));
                cleaned = truncated;
            }

            return cleaned;
        }

        /// <summary>
        /// Variant used during live pattern editing — no log spam on every keystroke.
        /// </summary>
        public string CleanNameSilent(string input)
        {
            if (string.IsNullOrEmpty(input)) return "Workset";
            string cleaned = input;
            char[] invalidChars = { ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '\t', '\n', '\r' };
            foreach (char c in invalidChars)
                cleaned = cleaned.Replace(c, '-');
            while (cleaned.Contains("--"))
                cleaned = cleaned.Replace("--", "-");
            cleaned = cleaned.Trim().Trim('-');
            if (string.IsNullOrEmpty(cleaned)) cleaned = "Workset";
            if (cleaned.Length > 150) cleaned = cleaned[..150];
            return cleaned;
        }

        // ─── Write operations ─────────────────────────────────────────────────

        /// <summary>
        /// Creates (or re-uses) one workset per request and assigns that link's
        /// instances to it, one Transaction per link. Every request lands in
        /// <paramref name="summary"/> as a <see cref="LinkOutcome"/> — including
        /// failures, which are rolled back and do not stop the run.
        /// </summary>
        /// <param name="summary">
        /// Pre-filled with Mode / Label / link counts (and any outcome the caller
        /// already decided, e.g. a Resync skip). The same instance is returned.
        /// </param>
        /// <param name="onProgress">Called after each link with (processed, total).</param>
        public RunSummary CreateAndAssign(
            Document doc,
            IEnumerable<WorksetRequest> requests,
            RunSummary summary,
            Action<int, int> onProgress = null)
        {
            summary ??= new RunSummary();

            var queue = requests?.ToList() ?? new List<WorksetRequest>();
            int total = queue.Count;
            int processed = 0;
            int committed = 0;

            if (total == 0)
            {
                summary.FinishedAt = DateTime.Now;
                return summary;
            }

            // One undo step for the whole run (ARCHITECTURE.md §5): each link keeps its
            // own Transaction inside the group, so a failing link still rolls back on its
            // own, but the user undoes a 7-link run with one Ctrl+Z instead of seven.
            // Assimilate only when something committed — RollBack() on the group would
            // undo the links that already succeeded.
            using TransactionGroup group = new(doc, $"{summary.Mode}: {summary.Label}");
            group.Start();

            foreach (WorksetRequest request in queue)
            {
                processed++;

                var outcome = new LinkOutcome
                {
                    LinkName    = request.LinkName,
                    WorksetName = request.ProposedName,
                    WasUnloaded = request.IsUnloaded
                };

                // Skip never opens a transaction. It is reported, not silent: the
                // summary names the link so "every link has a workset" stays checkable.
                if (request.Action == WorksetAction.Skip)
                {
                    outcome.Outcome = RunOutcome.Skipped;
                    outcome.Note    = "Skipped by choice — this link has no workset";
                    _log(new LogEntry(LogLevel.Warning,
                        $"Skipped '{request.LinkName}' by choice — it is left without a workset."));
                    summary.Outcomes.Add(outcome);
                    if (!summary.LinksWithoutWorkset.Contains(request.LinkName))
                        summary.LinksWithoutWorkset.Add(request.LinkName);
                    onProgress?.Invoke(processed, total);
                    continue;
                }

                using Transaction tx = new(doc, TransactionNameFor(request));
                try
                {
                    tx.Start();

                    // Always re-read: the pattern box can be edited after the scan, so
                    // request.CreateNew may be stale. V015 trusted it and Workset.Create
                    // threw on an existing name, turning a re-usable workset into an Error.
                    List<Workset> userWorksets = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .ToList();

                    Workset existing = userWorksets.FirstOrDefault(ws =>
                        ws.Name.Equals(request.ProposedName, StringComparison.OrdinalIgnoreCase));

                    // The workset a Rename or Adopt was aimed at, found by id — the name
                    // is the very thing a rename changes, so it cannot be the identity.
                    Workset matched = request.MatchedWorksetId == 0
                        ? null
                        : userWorksets.FirstOrDefault(ws => ws.Id.IntegerValue == request.MatchedWorksetId);

                    WorksetId targetId;
                    bool createdNew = false;
                    bool renamed    = false;
                    bool adopted    = false;

                    if (request.Action == WorksetAction.Adopt && matched != null)
                    {
                        // Keep the matched workset exactly as it is and assign into it.
                        targetId = matched.Id;
                        adopted  = true;
                        outcome.WorksetName = matched.Name;
                        _log(new LogEntry(LogLevel.Info,
                            $"Adopted existing workset '{matched.Name}' for '{request.LinkName}' — " +
                            "nothing created, nothing renamed."));
                    }
                    else if (request.Action == WorksetAction.Rename && matched != null)
                    {
                        if (matched.Name.Equals(request.ProposedName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Already correctly named — a rename would be a no-op.
                            targetId = matched.Id;
                            _log(new LogEntry(LogLevel.Info,
                                $"Workset '{matched.Name}' already carries the proposed name — re-used as is."));
                        }
                        else if (existing != null && existing.Id.IntegerValue != matched.Id.IntegerValue)
                        {
                            // Another workset already holds the target name. The pre-flight
                            // gate should have stopped this; renaming into it is impossible
                            // and assigning into it would silently merge two links. Refuse.
                            _log(new LogEntry(LogLevel.Error,
                                $"Cannot rename '{matched.Name}' to '{request.ProposedName}' — " +
                                $"another workset already uses that name. '{request.LinkName}' was left untouched."));
                            tx.RollBack();

                            outcome.Outcome = RunOutcome.Error;
                            outcome.Note    = $"'{request.ProposedName}' is already taken by another workset";
                            summary.Outcomes.Add(outcome);
                            onProgress?.Invoke(processed, total);
                            continue;
                        }
                        else
                        {
                            string previousName = matched.Name;
                            WorksetTable.RenameWorkset(doc, matched.Id, request.ProposedName);

                            targetId            = matched.Id;
                            renamed             = true;
                            outcome.RenamedFrom = previousName;

                            _log(new LogEntry(LogLevel.Info,
                                $"Renamed workset '{previousName}' → '{request.ProposedName}' " +
                                $"for '{request.LinkName}'"));
                        }
                    }
                    else if (existing != null)
                    {
                        targetId = existing.Id;
                        if (request.CreateNew)
                            _log(new LogEntry(LogLevel.Info,
                                $"Workset '{request.ProposedName}' already exists — re-used instead of created."));
                    }
                    else if (request.CreateNew)
                    {
                        Workset workset = Workset.Create(doc, request.ProposedName);
                        targetId   = workset.Id;
                        createdNew = true;
                        _log(new LogEntry(LogLevel.Info, $"Created workset: '{request.ProposedName}'"));
                    }
                    else
                    {
                        // Resync against a workset that is not there. Also the fallback when
                        // a Rename or Adopt target has vanished since the scan.
                        string reason = request.MatchedWorksetId != 0
                            ? $"the workset this link was matched to (id {request.MatchedWorksetId}) no longer exists"
                            : $"workset '{request.ProposedName}' not found";

                        _log(new LogEntry(LogLevel.Error, $"Skipped '{request.LinkName}' — {reason}."));
                        tx.RollBack();

                        outcome.Outcome = RunOutcome.Skipped;
                        outcome.Note    = request.MatchedWorksetId != 0
                            ? "Matched workset no longer exists — refresh and run again"
                            : "Workset does not exist — use Create";
                        summary.Outcomes.Add(outcome);
                        if (!summary.LinksWithoutWorkset.Contains(request.LinkName))
                            summary.LinksWithoutWorkset.Add(request.LinkName);
                        onProgress?.Invoke(processed, total);
                        continue;
                    }

                    AssignResult result = AssignLinkInstances(
                        doc, request.LinkTypeId, targetId, request.IsUnloaded);

                    outcome.InstancesAssigned = result.Assigned;
                    outcome.InstancesTotal    = result.Total;
                    outcome.Note              = result.Note;

                    if (result.Total == 0)
                    {
                        // Grid 3 — nothing placed in the model yet. A rename still counts
                        // as done: the workset exists under the right name, waiting.
                        outcome.Outcome = createdNew ? RunOutcome.CreatedNoInstances
                                        : renamed    ? RunOutcome.Renamed
                                        : adopted    ? RunOutcome.Adopted
                                                     : RunOutcome.Skipped;
                        outcome.Note    = "No instances placed — assignment happens when the link is placed";
                        _log(new LogEntry(LogLevel.Warning,
                            $"'{request.LinkName}' has no placed instances — workset '{request.ProposedName}' left empty."));
                    }
                    else if (result.Assigned == 0)
                    {
                        outcome.Outcome = RunOutcome.Missed;
                        _log(new LogEntry(LogLevel.Warning,
                            $"Missed '{request.LinkName}' — 0 of {result.Total} instance(s) could be assigned " +
                            $"to '{request.ProposedName}'. {result.Note}"));
                    }
                    else
                    {
                        outcome.Outcome = createdNew ? RunOutcome.Created
                                        : renamed    ? RunOutcome.Renamed
                                        : adopted    ? RunOutcome.Adopted
                                                     : RunOutcome.Reused;
                        _log(new LogEntry(LogLevel.Info,
                            $"Assigned {result.Assigned} instance(s) of '{request.LinkName}' → '{outcome.WorksetName}'"));
                        if (!string.IsNullOrWhiteSpace(result.Note))
                            _log(new LogEntry(LogLevel.Warning, $"'{request.LinkName}': {result.Note}"));
                    }

                    tx.Commit();
                    committed++;

                    // Only after the commit: a workset created in a transaction that
                    // then threw does not exist, so it must not be counted.
                    outcome.WorksetWasCreated = createdNew;
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    _log(new LogEntry(LogLevel.Error, $"Failed on '{request.ProposedName}': {ex.Message}"));

                    outcome.Outcome = RunOutcome.Error;
                    outcome.Note    = ex.Message;
                }

                // The only path that adds its outcome early is the
                // "workset not found" skip above, which also `continue`s.
                summary.Outcomes.Add(outcome);

                onProgress?.Invoke(processed, total);
            }

            if (committed > 0) group.Assimilate();
            else               group.RollBack();

            summary.FinishedAt = DateTime.Now;
            return summary;
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// The undo-stack label for one link's transaction. Named after the action so a
        /// user reading Revit's undo list sees what happened, not a generic "Resync".
        /// </summary>
        private static string TransactionNameFor(WorksetRequest request) => request.Action switch
        {
            WorksetAction.Rename => $"Rename workset: {request.RenameFrom} -> {request.ProposedName}",
            WorksetAction.Adopt  => $"Assign to workset: {request.RenameFrom}",
            WorksetAction.Create => $"Create workset: {request.ProposedName}",
            _                    => request.CreateNew
                                        ? $"Create workset: {request.ProposedName}"
                                        : $"Resync workset: {request.ProposedName}"
        };

        /// <summary>Instances written, instances found, and a note about the rest.</summary>
        private readonly record struct AssignResult(int Assigned, int Total, string Note);

        private AssignResult AssignLinkInstances(Document doc, int linkTypeId, WorksetId worksetId, bool isUnloaded)
        {
            List<RevitLinkInstance> instances = GetLinkInstances(doc, linkTypeId);
            int assigned = 0;
            int blocked  = 0;

            foreach (RevitLinkInstance instance in instances)
            {
                Parameter param = instance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (param == null || param.IsReadOnly)
                {
                    blocked++;
                    continue;
                }

                try
                {
                    if (param.Set(worksetId.IntegerValue)) assigned++;
                    else blocked++;
                }
                catch (Exception)
                {
                    // Pinned / borrowed by another user / otherwise refused —
                    // counted, never fatal to the rest of the run.
                    blocked++;
                }
            }

            string note;
            if (blocked == 0)
            {
                note = string.Empty;
            }
            else if (isUnloaded && assigned == 0)
            {
                // The honest reading when nothing at all went through on an unloaded
                // link: Revit would not take the write. Load it and run again.
                note = $"{blocked} instance(s) not assigned — the link is unloaded and Revit refused " +
                       "the workset change; load the link and run again";
            }
            else
            {
                note = $"{blocked} instance(s) not assigned (workset parameter read-only, pinned, " +
                       "or owned by another user)";
            }

            return new AssignResult(assigned, instances.Count, note);
        }

        /// <summary>
        /// Instances of one link type. Matched on GetTypeId(), NOT on the linked
        /// document's title: GetLinkDocument() is null while a link is unloaded,
        /// which is exactly the case V015 got wrong.
        /// </summary>
        private List<RevitLinkInstance> GetLinkInstances(Document doc, int linkTypeId)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(i => (i.GetTypeId()?.Value ?? ElementId.InvalidElementId.Value) == linkTypeId)
                .ToList();
        }

        private static Dictionary<int, string> BuildWorksetNameMap(Document doc)
        {
            var map = new Dictionary<int, string>();
            foreach (Workset ws in new FilteredWorksetCollector(doc))
                map[ws.Id.IntegerValue] = ws.Name;
            return map;
        }

        /// <summary>What the inclusion rule decided about a link's underlying file.</summary>
        private enum LinkFileKind
        {
            /// <summary>Ends in .rvt — a Revit file. Listed, and gets a workset.</summary>
            RevitFile,

            /// <summary>Positively something else (a bare .ifc, say). Never listed.</summary>
            NotRevitFile,

            /// <summary>No extension could be established. Listed anyway, with a Warning.</summary>
            Unknown
        }

        /// <summary>
        /// Applies the inclusion rule: ends in .rvt -> RevitFile.
        ///
        /// Both the link type's name and, where it resolves, the path on disk are tested,
        /// and either one ending in .rvt is enough — a link type that someone renamed by
        /// hand still points at a real .rvt file. Only when neither shows a .rvt and one
        /// of them shows some other extension is the link ruled out; an indeterminate
        /// case returns Unknown and is listed, because dropping a real link without
        /// saying so is the worst outcome available here.
        /// </summary>
        private static LinkFileKind ClassifyLinkFile(RevitLinkType type, string rawName)
        {
            bool sawOtherExtension = false;

            if (EndsWithRvt(rawName)) return LinkFileKind.RevitFile;
            if (HasAnyExtension(rawName)) sawOtherExtension = true;

            try
            {
                if (type.IsExternalFileReference())
                {
                    ExternalFileReference reference = type.GetExternalFileReference();
                    if (reference != null)
                    {
                        string path = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                            reference.GetAbsolutePath());

                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            if (EndsWithRvt(path)) return LinkFileKind.RevitFile;
                            if (HasAnyExtension(path)) sawOtherExtension = true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // An unresolvable reference tells us nothing either way — fall through.
            }

            return sawOtherExtension ? LinkFileKind.NotRevitFile : LinkFileKind.Unknown;
        }

        private static bool EndsWithRvt(string value) =>
            !string.IsNullOrEmpty(value) &&
            value.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase);

        private static bool HasAnyExtension(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            !string.IsNullOrEmpty(Path.GetExtension(value));

        /// <summary>
        /// Removes a trailing ".rvt" and nothing else.
        ///
        /// Deliberately not Path.GetFileNameWithoutExtension, which strips whatever
        /// follows the last dot: on "Z3.ifc.rvt" both give "Z3.ifc", but on a link named
        /// "Tower Phase V1.2" that method returns "Tower Phase V1" and this returns the
        /// name intact. The result is the matcher's anchor and the value {name} resolves
        /// to, so a wrong answer here propagates into every workset name.
        /// </summary>
        private static string StripRvtExtension(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            return EndsWithRvt(rawName)
                ? rawName[..^4]
                : rawName;
        }

        /// <summary>
        /// A nested link (a link inside a linked model). Its workset in the host is
        /// governed by the parent link, so there is nothing here for this tool to
        /// set — V015 listed them and created worksets that could never fill.
        /// </summary>
        private static bool IsNestedLink(RevitLinkType type)
        {
            try { return type.IsNestedLink; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// An IFC link. Revit does not link an .ifc directly: it converts the file
        /// into a shadow Revit model next to it ("Z3_IFC_21-08-2026.ifc.rvt") and
        /// links that, so the result is an ordinary RevitLinkType whose name still
        /// carries the .ifc.
        ///
        /// This no longer decides anything: a ".ifc.rvt" shadow file ends in .rvt, so it
        /// is listed and gets a workset like any Revit link (Rafi, 2026-10-18). The flag
        /// survives purely so the UI can tint the row, and so the report can say how many
        /// of the listed links came in from IFC.
        /// </summary>
        private static bool IsIfcLink(RevitLinkType type)
        {
            string name = type.Name ?? string.Empty;

            // "Z3.ifc.rvt" → stem "Z3.ifc";  a bare "Z3.ifc" name is covered too.
            if (name.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) return true;
            if (Path.GetFileNameWithoutExtension(name)
                    .EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) return true;

            // Belt and braces: ask the external reference for the path on disk.
            // Not every link type has one (and a broken one can throw), hence the guard.
            try
            {
                if (!type.IsExternalFileReference()) return false;

                ExternalFileReference reference = type.GetExternalFileReference();
                if (reference == null) return false;

                string path = ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetAbsolutePath());
                if (string.IsNullOrWhiteSpace(path)) return false;

                if (path.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) return true;
                if (Path.GetFileNameWithoutExtension(path)
                        .EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception)
            {
                // An unresolvable reference tells us nothing about the format —
                // fall through and treat it as an ordinary Revit link.
            }

            return false;
        }

        /// <summary>
        /// Maps Revit's LinkedFileStatus onto <see cref="LinkLoadState"/>.
        /// Compared by name rather than by enum member so this folder keeps
        /// compiling if the SDK's member set differs (LocallyUnloaded and friends).
        /// </summary>
        private static LinkLoadState MapLoadState(RevitLinkType type)
        {
            try
            {
                string status = type.GetLinkedFileStatus().ToString();

                if (status.Equals("Loaded", StringComparison.OrdinalIgnoreCase))
                    return LinkLoadState.Loaded;
                if (status.IndexOf("NotFound", StringComparison.OrdinalIgnoreCase) >= 0)
                    return LinkLoadState.NotFound;
                if (status.IndexOf("Unloaded", StringComparison.OrdinalIgnoreCase) >= 0)
                    return LinkLoadState.Unloaded;

                // "Invalid" and anything new: not loaded, but not provably missing.
                return LinkLoadState.Unloaded;
            }
            catch (Exception)
            {
                return LinkLoadState.Loaded;
            }
        }
    }
}
