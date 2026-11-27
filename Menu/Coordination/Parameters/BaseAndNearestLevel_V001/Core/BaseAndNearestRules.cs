using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core
{
    /// <summary>What one element row will do, and why.</summary>
    public readonly struct RowVerdict
    {
        /// <summary>Creates a verdict.</summary>
        public RowVerdict(RowState state, string text)
        {
            State = state;
            Text = text;
        }

        /// <summary>The row's state.</summary>
        public RowState State { get; }

        /// <summary>Status text for the grid.</summary>
        public string Text { get; }
    }

    /// <summary>
    /// The pre-checks and the per-row verdicts. Pure decisions over the facts, with no Revit API, so the
    /// window, the run and the unit tests all reach the same answer.
    /// </summary>
    public static class BaseAndNearestRules
    {
        /// <summary>The effective "existing values" choice for one row.</summary>
        public static ExistingChoice EffectiveChoice(ExistingChoice rowChoice, bool overwriteDefault) =>
            rowChoice != ExistingChoice.Default
                ? rowChoice
                : overwriteDefault ? ExistingChoice.Rewrite : ExistingChoice.Skip;

        /// <summary>What <paramref name="e"/> will do when the run starts.</summary>
        public static RowVerdict EvaluateRow(ElementFacts e, bool overwriteDefault, ExistingChoice rowChoice)
        {
            if (e == null) return new RowVerdict(RowState.Warning, "Skipped · no data");
            if (e.CategoryUnsupported)
                return new RowVerdict(RowState.Warning, $"Skipped · {e.Category} does not take bound parameters");
            if (!string.IsNullOrEmpty(e.OwnedBy))
                return new RowVerdict(RowState.Warning, $"Skipped · checked out by {e.OwnedBy}");
            if (e.OutOfDate)
                return new RowVerdict(RowState.Warning, "Skipped · changed in the central model; synchronise first");
            if (!e.HasBase)
                return new RowVerdict(RowState.Warning, "Skipped · no base level on this element");
            if (!e.NearestFound)
                return new RowVerdict(RowState.Warning, "Skipped · no candidate level matches the rule");

            string[] readOnly = TargetSlots.All
                .Where(s => e.SlotReadOnly[(int)s])
                .Select(TargetSlots.Label)
                .ToArray();
            if (readOnly.Length > 0)
                return new RowVerdict(RowState.Warning, "Skipped · read-only on this element: " + string.Join(", ", readOnly));

            bool allMatch = TargetSlots.All.All(s =>
                ValueText.Matches(e.CurrentValues[(int)s], e.NewValues[(int)s], TargetSlots.IsText(s)));
            if (allMatch) return new RowVerdict(RowState.UpToDate, "Up to date");

            if (e.AnyDifferent && EffectiveChoice(rowChoice, overwriteDefault) == ExistingChoice.Skip)
                return new RowVerdict(RowState.Warning, "Skipped · already has a different value (Skip)");

            return new RowVerdict(RowState.Ready, e.AnyDifferent ? "Ready · will rewrite" : "Ready");
        }

        /// <summary>Why <paramref name="e"/> cannot be re-hosted, given the shape-edit option.</summary>
        public static RehostSkip RehostBlockFor(ElementFacts e, bool includeShapeEdited)
        {
            if (e == null) return RehostSkip.NoBaseLevel;
            if (!e.HasBase || !e.NearestFound) return RehostSkip.NoBaseLevel;
            if (!e.IsChanged) return RehostSkip.NoChange;
            if (!string.IsNullOrEmpty(e.OwnedBy)) return RehostSkip.OwnedByOther;
            if (e.LevelParamReadOnly) return RehostSkip.ReadOnlyLevel;
            if (e.InGroup) return RehostSkip.InGroup;
            if (e.ShapeEdited && !includeShapeEdited) return RehostSkip.ShapeEdited;
            return RehostSkip.None;
        }

        /// <summary>Why an element is not re-hosted, as the dialog and the log word it.</summary>
        public static string RehostSkipText(RehostSkip skip) => skip switch
        {
            RehostSkip.NoChange => "Nearest level is already its base level",
            RehostSkip.ShapeEdited => "Shape-edited — changing Level can reset its point edits",
            RehostSkip.ReadOnlyLevel => "Its level parameter is read-only",
            RehostSkip.InGroup => "In a group — would change every instance of the group",
            RehostSkip.OwnedByOther => "Checked out by another user",
            RehostSkip.NoBaseLevel => "No base level to change",
            _ => string.Empty
        };

        /// <summary>
        /// The pre-check rows, in window order. A <see cref="CheckResult.Blocked"/> row stops the run.
        /// </summary>
        public static List<CheckRow> EvaluateChecks(FactsResult facts, RunRequest request)
        {
            var rows = new List<CheckRow>();
            if (facts == null || request == null)
            {
                rows.Add(new CheckRow("a · Document", CheckResult.Blocked, "Nothing has been read yet."));
                return rows;
            }

            DocumentFacts doc = facts.Document;

            // a · Document
            if (doc.IsFamilyDocument)
                rows.Add(new CheckRow("a · Document", CheckResult.Blocked, "This is a family document. The tool works on projects."));
            else if (doc.IsReadOnly)
                rows.Add(new CheckRow("a · Document", CheckResult.Blocked, "The document is read-only, so nothing can be written."));
            else
                rows.Add(new CheckRow("a · Document", CheckResult.Pass,
                    doc.IsWorkshared ? "Host project, editable, workshared." : "Host project, editable."));

            // b · Target parameters
            rows.Add(TargetCheck(facts));

            // c · Category binding
            rows.Add(BindingCheck(facts));

            // d · Levels
            rows.Add(LevelCheck(doc, request));

            // e · Elements
            rows.Add(ElementCheck(facts, request));

            // f · Worksharing
            rows.Add(WorksharingCheck(facts, doc));

            // g · Re-host
            rows.Add(RehostCheck(facts, request));

            return rows;
        }

        private static CheckRow TargetCheck(FactsResult facts)
        {
            var missing = new List<string>();
            var created = new List<string>();
            var problems = new List<string>();

            foreach (TargetSlot slot in TargetSlots.All)
            {
                SlotFacts s = facts.Slots[(int)slot];
                string label = $"{TargetSlots.Number(slot)} {TargetSlots.Label(slot)}";
                if (s == null || s.IsEmpty)
                {
                    missing.Add(label);
                    continue;
                }
                if (s.ProjectMatchCount > 1)
                {
                    problems.Add($"{label}: “{s.Name}” matches {s.ProjectMatchCount} project parameters; rename one or pick another.");
                    continue;
                }
                if (s.IsNew)
                {
                    problems.AddRange(FileProblems(s, label));
                    if (!FileProblems(s, label).Any())
                        created.Add($"{label} → “{s.Name}” ({TargetSlots.TypeName(slot)})");
                    continue;
                }
                if (!s.DataTypeOk)
                    problems.Add($"{label}: “{s.Name}” is {(string.IsNullOrEmpty(s.DataTypeName) ? "another type" : s.DataTypeName)}, not {TargetSlots.TypeName(slot)}.");
                else if (!s.IsInstance)
                    problems.Add($"{label}: “{s.Name}” is a type parameter. The tool writes instance values.");
            }

            if (missing.Count > 0)
                return new CheckRow("b · Target parameters", CheckResult.Blocked,
                    "No parameter chosen for: " + string.Join(", ", missing) + ".");
            if (problems.Count > 0)
                return new CheckRow("b · Target parameters", CheckResult.Blocked, string.Join(" ", problems));
            if (created.Count > 0)
                return new CheckRow("b · Target parameters", CheckResult.Warning,
                    $"{4 - created.Count} found. Will be created as shared instance parameters: {string.Join("; ", created)}.");
            return new CheckRow("b · Target parameters", CheckResult.Pass, "All four found, shared instance parameters of the right type.");
        }

        private static IEnumerable<string> FileProblems(SlotFacts s, string label)
        {
            FileFacts f = s.File;
            if (string.IsNullOrWhiteSpace(f.Path))
                yield return $"{label}: no shared parameter file chosen for the new parameter “{s.Name}”.";
            else if (!string.IsNullOrEmpty(f.ReadError))
                yield return $"{label}: the shared parameter file cannot be read ({f.ReadError}).";
            else if (!f.Exists)
                yield return $"{label}: the shared parameter file does not exist.";
            else if (!f.Writable)
                yield return $"{label}: the shared parameter file cannot be written. Close it or clear its read-only flag.";
            else if (f.MatchCount > 1)
                yield return $"{label}: the shared parameter file holds {f.MatchCount} entries called “{s.Name}”.";
            else if (f.Entry != null && !f.Entry.Fits(s.Slot))
                yield return $"{label}: “{s.Name}” is already in the shared parameter file as {f.Entry.DataType}, not {TargetSlots.TypeName(s.Slot)}.";
            else if (!string.IsNullOrEmpty(f.GuidUsedBy))
                yield return $"{label}: the file entry's GUID is already used by the project parameter “{f.GuidUsedBy}”.";
        }

        private static CheckRow BindingCheck(FactsResult facts)
        {
            var extend = new List<string>();
            foreach (TargetSlot slot in TargetSlots.All)
            {
                SlotFacts s = facts.Slots[(int)slot];
                if (s == null || s.IsNew || s.MissingCategories.Count == 0) continue;
                extend.Add($"{TargetSlots.Number(slot)} {TargetSlots.Label(slot)}: {string.Join(", ", s.MissingCategories)}");
            }
            if (extend.Count > 0)
                return new CheckRow("c · Category binding", CheckResult.Warning,
                    "Categories will be added to the binding of " + string.Join("; ", extend) + ".");

            var unsupported = facts.Elements.Where(e => e.CategoryUnsupported).Select(e => e.Category).Distinct().ToList();
            if (unsupported.Count > 0)
                return new CheckRow("c · Category binding", CheckResult.Warning,
                    "Does not take bound parameters, will be skipped: " + string.Join(", ", unsupported) + ".");
            return new CheckRow("c · Category binding", CheckResult.Pass, "Roofs, Floors and Walls are covered by all four parameters.");
        }

        private static CheckRow LevelCheck(DocumentFacts doc, RunRequest request)
        {
            int count = doc.Candidates.Count;
            if (count == 0)
                return new CheckRow("d · Levels", CheckResult.Blocked, request.BuildingStoreysOnly
                    ? "No building-storey level in this project. Untick “Building-storey levels only” to use every level."
                    : "This project has no levels, so no nearest level can be worked out.");

            string excluded = doc.ExcludedLevels.Count > 0
                ? $" {doc.ExcludedLevels.Count} level(s) excluded by the filter: {string.Join(", ", doc.ExcludedLevels)}."
                : string.Empty;
            string detail = $"{count} candidate level(s): {string.Join(", ", doc.Candidates.OrderBy(c => c.ElevationMm).Select(c => c.Name))}." +
                            excluded + $" Rule: {NearestLevelCalculator.RuleLabel(request.Rule)}.";
            return count == 1
                ? new CheckRow("d · Levels", CheckResult.Warning, "Only one candidate level, so every element reports that level." + excluded)
                : new CheckRow("d · Levels", CheckResult.Pass, detail);
        }

        private static CheckRow ElementCheck(FactsResult facts, RunRequest request)
        {
            int listed = facts.Elements.Count;
            if (listed == 0)
                return new CheckRow("e · Elements", CheckResult.Blocked, "No roof, floor or wall is ticked.");

            int ready = facts.Elements.Count(e =>
                EvaluateRow(e, request.Overwrite, request.ChoiceFor(e.Id)).State == RowState.Ready);
            int noBase = facts.Elements.Count(e => !e.HasBase);
            var parts = new List<string>();
            if (noBase > 0) parts.Add($"{noBase} with no base level");
            if (facts.Missing.Count > 0) parts.Add($"{facts.Missing.Count} no longer in the model");

            if (ready == 0)
                return new CheckRow("e · Elements", CheckResult.Blocked,
                    $"None of the {listed} ticked elements is ready to write" +
                    (parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : string.Empty) + ".");
            return parts.Count > 0
                ? new CheckRow("e · Elements", CheckResult.Warning,
                    $"{ready} of {listed} will be written. Skipped: {string.Join(", ", parts)}.")
                : new CheckRow("e · Elements", CheckResult.Pass, $"{ready} of {listed} will be written.");
        }

        private static CheckRow WorksharingCheck(FactsResult facts, DocumentFacts doc)
        {
            if (!doc.IsWorkshared)
                return new CheckRow("f · Worksharing", CheckResult.Pass, "Not a workshared model.");
            var owned = facts.Elements.Where(e => !string.IsNullOrEmpty(e.OwnedBy)).ToList();
            int stale = facts.Elements.Count(e => e.OutOfDate);
            if (owned.Count == 0 && stale == 0)
                return new CheckRow("f · Worksharing", CheckResult.Pass, $"All {facts.Elements.Count} editable. None owned by another user.");
            var parts = new List<string>();
            if (owned.Count > 0) parts.Add($"{owned.Count} checked out by {string.Join(", ", owned.Select(e => e.OwnedBy).Distinct())}");
            if (stale > 0) parts.Add($"{stale} changed in the central model");
            return new CheckRow("f · Worksharing", CheckResult.Warning, "Will be skipped: " + string.Join("; ", parts) + ".");
        }

        private static CheckRow RehostCheck(FactsResult facts, RunRequest request)
        {
            var changed = facts.Elements.Where(e => e.IsChanged).ToList();
            if (changed.Count == 0)
                return new CheckRow("g · Re-host", CheckResult.Pass,
                    "Every element's nearest level is already its base level, so there is nothing to re-host.");

            var blocked = changed
                .Select(e => RehostBlockFor(e, request.RehostShapeEdited))
                .Where(b => b != RehostSkip.None && b != RehostSkip.NoChange)
                .GroupBy(b => b)
                .Select(g => $"{g.Count()} {RehostSkipText(g.Key).ToLowerInvariant()}")
                .ToList();
            int canRehost = changed.Count(e => RehostBlockFor(e, request.RehostShapeEdited) == RehostSkip.None);

            string detail = $"{changed.Count} element(s) sit nearer another level. Re-host would change {canRehost}";
            detail += blocked.Count > 0 ? $" and skip: {string.Join(", ", blocked)}." : ".";
            detail += " Re-host is a separate action and changes nothing until you run it.";
            return new CheckRow("g · Re-host", blocked.Count > 0 ? CheckResult.Warning : CheckResult.Pass, detail);
        }

        /// <summary>True when any row blocks the run.</summary>
        public static bool AnyBlocked(IEnumerable<CheckRow> checks) =>
            checks != null && checks.Any(c => c.Result == CheckResult.Blocked);

        /// <summary>One-line summary of the pre-checks for the card header and the log.</summary>
        public static string Summary(IReadOnlyList<CheckRow> checks)
        {
            if (checks == null || checks.Count == 0) return "Not checked yet";
            int blocked = checks.Count(c => c.Result == CheckResult.Blocked);
            int warnings = checks.Count(c => c.Result == CheckResult.Warning);
            if (blocked > 0) return $"Blocked · {blocked} blocking, {warnings} warning(s)";
            return warnings > 0 ? $"Can run · {warnings} warning(s)" : "Can run · all checks pass";
        }
    }
}
