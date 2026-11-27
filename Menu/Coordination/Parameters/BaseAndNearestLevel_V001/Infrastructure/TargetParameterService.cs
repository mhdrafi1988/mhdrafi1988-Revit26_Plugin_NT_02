using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;
using Revit26_Plugin_Revised.Shared.Services;
using LogLevel = Revit26_Plugin_Revised.Shared.Models.LogLevel;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure
{
    /// <summary>One parameter already bound in the project, as the slot pickers list it.</summary>
    public sealed class ProjectParameterOption
    {
        /// <summary>Parameter name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>"Shared · Instance · Length" and so on.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>The parameter is Text.</summary>
        public bool IsText { get; set; }

        /// <summary>The parameter is Length.</summary>
        public bool IsLength { get; set; }

        /// <summary>The parameter is an instance parameter.</summary>
        public bool IsInstance { get; set; }

        /// <summary>True when this parameter can serve <paramref name="slot"/>.</summary>
        public bool Fits(TargetSlot slot) => IsInstance && (TargetSlots.IsText(slot) ? IsText : IsLength);

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Reads the facts behind the pre-checks and writes the four target parameters. Everything here runs on
    /// the Revit API thread, inside the tool's external event.
    /// </summary>
    public static class TargetParameterService
    {
        private const string TransactionName = "Base and Nearest Level";

        /// <summary>Every parameter bound in the project, sorted by name.</summary>
        public static List<ProjectParameterOption> ListProjectParameters(Document doc)
        {
            var list = new List<ProjectParameterOption>();
            for (DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator(); it.MoveNext();)
            {
                Definition def = it.Key;
                bool shared = def is InternalDefinition idef && doc.GetElement(idef.Id) is SharedParameterElement;
                bool instance = it.Current is InstanceBinding;
                ForgeTypeId spec = SafeSpec(def);
                list.Add(new ProjectParameterOption
                {
                    Name = def.Name,
                    IsText = spec == SpecTypeId.String.Text,
                    IsLength = spec == SpecTypeId.Length,
                    IsInstance = instance,
                    Kind = $"{(shared ? "Shared" : "Project")} · {(instance ? "Instance" : "Type")} · {SafeLabel(def)}"
                });
            }
            return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Reads the document, the four slots, the candidate levels and every ticked element in one pass.
        /// </summary>
        public static FactsResult GatherFacts(Document doc, RunRequest request)
        {
            var result = new FactsResult();
            DocumentFacts d = result.Document;
            d.IsFamilyDocument = doc.IsFamilyDocument;
            d.IsReadOnly = doc.IsReadOnly;
            d.IsWorkshared = doc.IsWorkshared;

            List<LevelCandidate> candidates = LevelCandidateService.List(doc, request.BuildingStoreysOnly, out List<string> excluded);
            d.Candidates.AddRange(candidates);
            d.ExcludedLevels.AddRange(excluded);

            // Slots: resolve the chosen name against the project's bindings.
            var definitions = new Definition[4];
            var bindings = new ElementBinding[4];
            foreach (TargetSlot slot in TargetSlots.All)
            {
                int i = (int)slot;
                var s = new SlotFacts { Slot = slot, Name = request.NameFor(slot), Pending = request.Pending[i] };
                result.Slots[i] = s;
                if (s.IsEmpty) continue;

                List<(Definition Def, ElementBinding Binding)> matches = FindBindings(doc, s.Name);
                s.ProjectMatchCount = matches.Count;
                s.IsNew = matches.Count == 0 && s.Pending != null &&
                          string.Equals(s.Pending.Name?.Trim(), s.Name, StringComparison.OrdinalIgnoreCase);

                if (matches.Count == 1)
                {
                    (definitions[i], bindings[i]) = matches[0];
                    ForgeTypeId spec = SafeSpec(definitions[i]);
                    s.DataTypeOk = TargetSlots.IsText(slot) ? spec == SpecTypeId.String.Text : spec == SpecTypeId.Length;
                    s.DataTypeName = SafeLabel(definitions[i]);
                    s.IsInstance = bindings[i] is InstanceBinding;
                    if (definitions[i] is InternalDefinition idef)
                    {
                        s.VariesAcrossGroups = idef.VariesAcrossGroups;
                        s.IsShared = doc.GetElement(idef.Id) is SharedParameterElement;
                    }
                }
                else if (s.IsNew)
                {
                    s.VariesAcrossGroups = true;                 // set when the parameter is created
                    s.File.Path = s.Pending.FilePath ?? string.Empty;
                    ReadFileFacts(doc, s);
                }
            }

            // Elements.
            var categories = new Dictionary<long, Category>();
            int done = 0;
            foreach (long id in request.ElementIds)
            {
                ToolCancellation.CheckCurrent();
                ToolCancellation.ReportCurrent(++done, request.ElementIds.Count, $"Reading {done} of {request.ElementIds.Count}…");

                Element e = doc.GetElement(new ElementId(id));
                if (e?.Category == null)
                {
                    result.Missing.Add(id);
                    continue;
                }
                categories[e.Category.Id.Value] = e.Category;
                result.Elements.Add(ReadElement(doc, e, request, candidates, definitions, d.IsWorkshared));
            }

            // Which of those categories each existing binding is missing.
            foreach (TargetSlot slot in TargetSlots.All)
            {
                int i = (int)slot;
                if (bindings[i] == null) continue;
                foreach (Category c in categories.Values.OrderBy(c => c.Name))
                    if (c.AllowsBoundParameters && !bindings[i].Categories.Contains(c))
                        result.Slots[i].MissingCategories.Add(c.Name);
            }
            return result;
        }

        /// <summary>
        /// Re-runs the pre-checks, then creates or extends the four parameters and writes their values in one
        /// transaction. A per-element write failure is reported and the run goes on; anything else propagates,
        /// so the transaction rolls back and nothing is half-written.
        /// </summary>
        public static RunResult WriteParameters(Document doc, RunRequest request)
        {
            var run = new RunResult();
            FactsResult facts = GatherFacts(doc, request);
            run.Facts = facts;

            List<CheckRow> checks = BaseAndNearestRules.EvaluateChecks(facts, request);
            if (BaseAndNearestRules.AnyBlocked(checks))
            {
                run.NotStartedReason = "A pre-check is blocked: " + string.Join(" ",
                    checks.Where(c => c.Result == CheckResult.Blocked).Select(c => $"{c.Title}: {c.Detail}"));
                return run;
            }

            var ready = new List<ElementFacts>();
            foreach (ElementFacts e in facts.Elements)
            {
                RowVerdict v = BaseAndNearestRules.EvaluateRow(e, request.Overwrite, request.ChoiceFor(e.Id));
                if (v.State == RowState.Ready) ready.Add(e);
                else run.Rows.Add(new RunRowResult { Id = e.Id, State = RowState.Skipped, Text = v.Text });
            }
            foreach (long id in facts.Missing)
                run.Rows.Add(new RunRowResult { Id = id, State = RowState.Skipped, Text = "Skipped · element no longer exists" });

            if (ready.Count == 0)
            {
                run.NotStartedReason = "No element is ready to write; nothing was changed.";
                return run;
            }

            // Shared parameter file entries are written before the transaction: a file cannot roll back.
            var newDefinitions = new ExternalDefinition[4];
            foreach (TargetSlot slot in TargetSlots.All)
                if (facts.Slots[(int)slot].IsNew)
                    newDefinitions[(int)slot] = EnsureFileDefinition(doc, facts.Slots[(int)slot], run);

            using (var tx = new Transaction(doc, TransactionName))
            {
                tx.Start();

                var targets = new InternalDefinition[4];
                foreach (TargetSlot slot in TargetSlots.All)
                {
                    int i = (int)slot;
                    targets[i] = facts.Slots[i].IsNew
                        ? BindNew(doc, newDefinitions[i], facts.Slots[i], request, run)
                        : ExtendBinding(doc, facts.Slots[i], request, run);
                }

                int done = 0;
                var rewrites = new List<string>();
                foreach (ElementFacts e in ready)
                {
                    ToolCancellation.CheckCurrent();
                    ToolCancellation.ReportCurrent(++done, ready.Count, $"Writing {done} of {ready.Count}…");

                    RunRowResult row = AssignAll(doc, e, targets);
                    run.Rows.Add(row);
                    run.ValuesWritten += row.Written;
                    if (row.State == RowState.Assigned && e.AnyDifferent)
                    {
                        row.Text = "Assigned · rewrote existing values";
                        rewrites.Add(e.Id.ToString());
                    }
                }
                if (rewrites.Count > 0)
                    run.Log.Add((LogLevel.Info, $"Rewrote existing values on {rewrites.Count} element(s): {string.Join(", ", rewrites)}."));

                TransactionStatus status = tx.Commit();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException($"Revit did not commit the changes (status {status}). Nothing was changed.");
            }

            run.Committed = true;
            return run;
        }

        /// <summary>
        /// Writes the four values of <paramref name="e"/> through <paramref name="targets"/>. Used by the
        /// write run and, after a re-host, to refresh the parameters inside the same transaction.
        /// </summary>
        internal static RunRowResult AssignAll(Document doc, ElementFacts e, InternalDefinition[] targets)
        {
            var row = new RunRowResult { Id = e.Id, State = RowState.Assigned, Text = "Assigned" };
            Element element = doc.GetElement(new ElementId(e.Id));
            if (element == null)
                return new RunRowResult { Id = e.Id, State = RowState.Skipped, Text = "Skipped · element no longer exists" };

            foreach (TargetSlot slot in BaseAndNearestRules.WrittenSlots(e))
            {
                int i = (int)slot;
                Parameter p = targets[i] == null ? null : element.get_Parameter(targets[i]);
                if (p == null)
                {
                    row.State = RowState.Failed;
                    row.Text = $"Failed · {TargetSlots.Label(slot)} not found on the element";
                    return row;
                }
                if (p.IsReadOnly)
                {
                    row.State = RowState.Skipped;
                    row.Text = $"Skipped · {TargetSlots.Label(slot)} is read-only on this element";
                    return row;
                }
                try
                {
                    bool set = TargetSlots.IsText(slot)
                        ? p.Set(e.NewValues[i])
                        : p.Set(UnitsMm.ToFeet(slot == TargetSlot.BaseOffset ? e.BaseOffsetMm : e.NearestOffsetMm));
                    if (!set)
                    {
                        row.State = RowState.Failed;
                        row.Text = $"Failed · Revit refused the value for {TargetSlots.Label(slot)}";
                        return row;
                    }
                    row.Written++;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    row.State = RowState.Failed;
                    row.Text = $"Failed · {TargetSlots.Label(slot)}: {ex.Message}";
                    return row;
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    row.State = RowState.Failed;
                    row.Text = $"Failed · {TargetSlots.Label(slot)}: {ex.Message}";
                    return row;
                }
            }
            if (!e.OffsetKnown) row.Text = "Assigned · level names only (" + LevelTiers.Label(e.Tier) + ")";
            return row;
        }

        /// <summary>The bound definitions of the four slots, for a run that already knows they exist.</summary>
        internal static InternalDefinition[] ResolveTargets(Document doc, FactsResult facts)
        {
            var targets = new InternalDefinition[4];
            foreach (TargetSlot slot in TargetSlots.All)
            {
                SlotFacts s = facts.Slots[(int)slot];
                if (s == null || s.IsEmpty) continue;
                targets[(int)slot] = FindBindings(doc, s.Name).Select(m => m.Def).OfType<InternalDefinition>().FirstOrDefault();
            }
            return targets;
        }

        // ── reading ───────────────────────────────────────────────────────────

        private static ElementFacts ReadElement(Document doc, Element e, RunRequest request,
            List<LevelCandidate> candidates, Definition[] definitions, bool workshared)
        {
            var f = new ElementFacts
            {
                Id = e.Id.Value,
                Category = e.Category.Name,
                FamilyAndType = doc.GetElement(e.GetTypeId()) is ElementType t
                    ? (string.IsNullOrEmpty(t.FamilyName) ? t.Name : $"{t.FamilyName}: {t.Name}")
                    : e.Name ?? string.Empty,
                InGroup = e.GroupId != ElementId.InvalidElementId,
                CategoryUnsupported = !e.Category.AllowsBoundParameters,
                ShapeEdited = LevelPairReader.IsShapeEdited(e),
                IsInPlace = LevelPairReader.IsInPlace(e),
                Placement = LevelPairReader.PlacementOf(e)?.ToString() ?? string.Empty
            };

            BasePlacement placement = LevelPairReader.Read(doc, e);
            f.HasBase = placement.Found;
            f.Tier = placement.Tier;
            if (placement.Found)
            {
                f.BaseLevelId = placement.LevelId;
                f.BaseLevelName = placement.LevelName;
                f.Source = placement.Source;
                f.LevelParamReadOnly = placement.ReadOnly;

                if (placement.OffsetKnown)
                {
                    f.BaseOffsetMm = placement.OffsetMm;
                    f.ElevationMm = placement.ElevationMm;

                    NearestMatch nearest = NearestLevelCalculator.Find(candidates, f.ElevationMm, request.Rule, request.ToleranceMm);
                    f.NearestFound = nearest.Found;
                    f.NearestLevelId = nearest.LevelId;
                    f.NearestLevelName = nearest.LevelName;
                    f.NearestOffsetMm = nearest.OffsetMm;
                }
                else
                {
                    // Tier 2 and 3: the level is known but its offset is not measured from it, so the
                    // nearest level is the level's own nearest neighbour by elevation and no offset is set.
                    NearestMatch nearest = NearestLevelCalculator.Find(candidates, placement.LevelElevationMm, request.Rule, request.ToleranceMm);
                    f.NearestFound = nearest.Found;
                    f.NearestLevelId = nearest.LevelId;
                    f.NearestLevelName = nearest.LevelName;
                }
            }

            foreach (TargetSlot slot in TargetSlots.All)
            {
                int i = (int)slot;
                f.NewValues[i] = ValueText.For(f, slot);
                if (definitions[i] == null) continue;
                Parameter p = e.get_Parameter(definitions[i]);
                if (p == null) continue;
                f.CurrentValues[i] = p.StorageType == StorageType.String
                    ? p.AsString() ?? string.Empty
                    : p.AsValueString() ?? string.Empty;
                f.SlotReadOnly[i] = p.IsReadOnly;
            }
            f.HasAnyValue = f.CurrentValues.Any(v => !string.IsNullOrWhiteSpace(v));
            f.AnyDifferent = BaseAndNearestRules.WrittenSlots(f).Any(s =>
                !string.IsNullOrWhiteSpace(f.CurrentValues[(int)s]) &&
                !ValueText.Matches(f.CurrentValues[(int)s], f.NewValues[(int)s], TargetSlots.IsText(s)));

            if (workshared)
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, e.Id, out string owner);
                if (status == CheckoutStatus.OwnedByOtherUser) f.OwnedBy = string.IsNullOrEmpty(owner) ? "another user" : owner;
                ModelUpdatesStatus updates = WorksharingUtils.GetModelUpdatesStatus(doc, e.Id);
                f.OutOfDate = updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral;
            }

            f.RehostBlock = BaseAndNearestRules.RehostBlockFor(f, request.RehostShapeEdited);
            return f;
        }

        private static void ReadFileFacts(Document doc, SlotFacts s)
        {
            FileFacts f = s.File;
            if (string.IsNullOrWhiteSpace(f.Path)) return;
            try
            {
                f.Exists = File.Exists(f.Path);
                if (!f.Exists) return;
                f.Writable = SharedParamFileReader.IsWritable(f.Path);
                List<SharedParamFileEntry> matches = SharedParamFileReader.FindAll(SharedParamFileReader.Read(f.Path), s.Name);
                f.MatchCount = matches.Count;
                f.Entry = matches.FirstOrDefault();
                if (f.Entry != null)
                {
                    SharedParameterElement used = SharedParameterElement.Lookup(doc, f.Entry.Guid);
                    if (used != null && !string.Equals(used.Name, s.Name, StringComparison.OrdinalIgnoreCase))
                        f.GuidUsedBy = used.Name;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                f.ReadError = ex.Message;
            }
        }

        // ── creating and binding ──────────────────────────────────────────────

        private static ExternalDefinition EnsureFileDefinition(Document doc, SlotFacts slot, RunResult run)
        {
            NewParameterSpec spec = slot.Pending;
            using var scope = new SharedParameterFileScope(doc.Application, spec.FilePath);

            foreach (DefinitionGroup g in scope.File.Groups)
            {
                foreach (Definition d in g.Definitions)
                {
                    if (d is ExternalDefinition ext && string.Equals(ext.Name, spec.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        run.Log.Add((LogLevel.Info, $"“{ext.Name}” found in {Path.GetFileName(spec.FilePath)} (group “{g.Name}”); GUID {ext.GUID} reused."));
                        return ext;
                    }
                }
            }

            string groupName = string.IsNullOrWhiteSpace(spec.FileGroup) ? "Data" : spec.FileGroup.Trim();
            DefinitionGroup group = scope.File.Groups.get_Item(groupName) ?? scope.File.Groups.Create(groupName);
            ForgeTypeId dataType = TargetSlots.IsText(spec.Slot) ? SpecTypeId.String.Text : SpecTypeId.Length;
            var options = new ExternalDefinitionCreationOptions(spec.Name, dataType)
            {
                Description = $"{TargetSlots.Label(spec.Slot)}, written by {ToolCatalog.BaseAndNearestLevel.Name}.",
                UserModifiable = true,
                Visible = true
            };
            var created = (ExternalDefinition)group.Definitions.Create(options);
            run.Log.Add((LogLevel.Info, $"Added “{spec.Name}” ({TargetSlots.TypeName(spec.Slot)}, GUID {created.GUID}) to {Path.GetFileName(spec.FilePath)} / {groupName}. The file keeps this entry even if the run is undone."));
            return created;
        }

        private static InternalDefinition BindNew(Document doc, ExternalDefinition definition, SlotFacts slot, RunRequest request, RunResult run)
        {
            CategorySet categories = RequestCategories(doc, request, run);
            if (categories.IsEmpty)
                throw new InvalidOperationException($"“{definition.Name}” has no category to bind to. Nothing was changed in the project.");

            InstanceBinding binding = doc.Application.Create.NewInstanceBinding(categories);
            if (!doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Data))
                throw new InvalidOperationException($"Revit rejected the binding of “{definition.Name}”.");

            InternalDefinition target = FindBindings(doc, definition.Name).Select(m => m.Def).OfType<InternalDefinition>().FirstOrDefault()
                ?? throw new InvalidOperationException($"“{definition.Name}” was bound but cannot be found in the project.");
            target.SetAllowVaryBetweenGroups(doc, true);
            run.Log.Add((LogLevel.Success, $"Created “{definition.Name}”: shared, instance, {TargetSlots.TypeName(slot.Slot)}, Properties group Data, values can vary by group; bound to {categories.Size} categor{(categories.Size == 1 ? "y" : "ies")}."));
            return target;
        }

        private static InternalDefinition ExtendBinding(Document doc, SlotFacts slot, RunRequest request, RunResult run)
        {
            (Definition def, ElementBinding binding) = FindBindings(doc, slot.Name).Single();
            if (slot.MissingCategories.Count > 0)
            {
                foreach (Category c in RequestCategories(doc, request, run).Cast<Category>())
                    if (!binding.Categories.Contains(c)) binding.Categories.Insert(c);
                if (!doc.ParameterBindings.ReInsert(def, binding, def.GetGroupTypeId()))
                    throw new InvalidOperationException($"Revit rejected the new categories for “{def.Name}”.");
                run.Log.Add((LogLevel.Info, $"Added {string.Join(", ", slot.MissingCategories)} to the binding of “{def.Name}”."));
            }
            return def as InternalDefinition
                ?? throw new InvalidOperationException($"“{def.Name}” is not an internal definition.");
        }

        /// <summary>
        /// The categories the four parameters are bound to: the ones the run actually covers, so the
        /// parameters do not appear on every element in the model (1.1.0).
        /// </summary>
        private static CategorySet RequestCategories(Document doc, RunRequest request, RunResult run)
        {
            var wanted = new HashSet<string>(request?.Categories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var set = new CategorySet();
            foreach (Category c in doc.Settings.Categories)
            {
                if (c == null || !wanted.Contains(c.Name)) continue;
                if (c.AllowsBoundParameters) set.Insert(c);
                else run?.Log.Add((LogLevel.Warning, $"{c.Name} does not allow bound parameters; left out of the binding."));
            }
            return set;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static List<(Definition Def, ElementBinding Binding)> FindBindings(Document doc, string name)
        {
            var list = new List<(Definition, ElementBinding)>();
            if (string.IsNullOrWhiteSpace(name)) return list;
            for (DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator(); it.MoveNext();)
            {
                if (it.Current is ElementBinding b && string.Equals(it.Key.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                    list.Add((it.Key, b));
            }
            return list;
        }

        private static ForgeTypeId SafeSpec(Definition def)
        {
            try { return def.GetDataType(); }
            catch { return new ForgeTypeId(); }
        }

        private static string SafeLabel(Definition def)
        {
            try
            {
                ForgeTypeId spec = def.GetDataType();
                if (spec == SpecTypeId.String.Text) return "Text";
                if (spec == SpecTypeId.Length) return "Length";
                return LabelUtils.GetLabelForSpec(spec);
            }
            catch
            {
                return "Other";
            }
        }
    }
}
