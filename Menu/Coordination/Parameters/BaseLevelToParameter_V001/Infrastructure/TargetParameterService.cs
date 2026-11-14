using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.Infrastructure
{
    /// <summary>A parameter bound in the project, for the target combo box.</summary>
    public sealed class ProjectParameterOption
    {
        /// <summary>Parameter name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>"Shared · Instance · Text" style description.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>What the window asks the Revit side to check or run.</summary>
    public sealed class TargetRequest
    {
        /// <summary>Target parameter name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The pending new parameter, or null.</summary>
        public NewParameterSpec Pending { get; set; }

        /// <summary>Ids of the ticked elements.</summary>
        public List<long> ElementIds { get; set; } = new();

        /// <summary>"Existing values" option: true = Rewrite, false = Skip (the default for rows without their own choice).</summary>
        public bool Overwrite { get; set; }

        /// <summary>
        /// Per-element choice for an existing value (1.2.0). Only ids with a choice other than
        /// <see cref="ExistingChoice.Default"/> need to be listed.
        /// </summary>
        public Dictionary<long, ExistingChoice> Choices { get; set; } = new();

        /// <summary>
        /// "Clear old values, write only new values" (1.3.0): every different value is rewritten (row choices are
        /// ignored) and the value of an element without a base level is cleared.
        /// </summary>
        public bool ClearOld { get; set; }

        /// <summary>The choice for element <paramref name="id"/> (Default when none is set).</summary>
        public ExistingChoice ChoiceFor(long id) =>
            Choices != null && Choices.TryGetValue(id, out ExistingChoice c) ? c : ExistingChoice.Default;

        /// <summary>Value format.</summary>
        public ValueFormat Format { get; set; } = ValueFormat.LevelParenOffset;

        /// <summary>Custom pattern, used when <see cref="Format"/> is <see cref="ValueFormat.Custom"/>.</summary>
        public string CustomPattern { get; set; } = ValueFormatter.DefaultCustomPattern;

        /// <summary>The value for <paramref name="f"/> in this request's format.</summary>
        public string ValueFor(ElementFacts f) => ValueFormatter.Format(f.LevelName, f.OffsetMm, Format, CustomPattern);
    }

    /// <summary>Result of reading the target, the document and the ticked elements.</summary>
    public sealed class FactsResult
    {
        /// <summary>Target, document and file facts.</summary>
        public TargetFacts Target { get; set; } = new();

        /// <summary>Facts per ticked element that still exists.</summary>
        public List<ElementFacts> Elements { get; set; } = new();

        /// <summary>Ids that no longer exist in the model.</summary>
        public List<long> Missing { get; set; } = new();
    }

    /// <summary>Result of a run.</summary>
    public sealed class RunResult
    {
        /// <summary>True when the transaction was committed.</summary>
        public bool Committed { get; set; }

        /// <summary>Message when the run did not start (for example a blocking pre-check), else null.</summary>
        public string NotStartedReason { get; set; }

        /// <summary>Outcome per ticked element.</summary>
        public List<RunRowResult> Rows { get; set; } = new();

        /// <summary>Log lines (level, text).</summary>
        public List<(Shared.Models.LogLevel Level, string Text)> Log { get; } = new();

        /// <summary>The facts the run was based on (also used to refresh the grid).</summary>
        public FactsResult Facts { get; set; }
    }

    /// <summary>
    /// Revit-side work on the target parameter: lists project parameters, gathers the facts for the
    /// pre-checks and runs the assignment. Every method runs on the Revit API thread.
    /// </summary>
    public static class TargetParameterService
    {
        private const string TransactionName = "Base Level to Parameter";

        /// <summary>Every parameter bound in the project, sorted by name.</summary>
        public static List<ProjectParameterOption> ListProjectParameters(Document doc)
        {
            var list = new List<ProjectParameterOption>();
            for (DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator(); it.MoveNext();)
            {
                Definition def = it.Key;
                bool shared = def is InternalDefinition idef && doc.GetElement(idef.Id) is SharedParameterElement;
                string type = SafeLabel(def);
                list.Add(new ProjectParameterOption
                {
                    Name = def.Name,
                    Kind = $"{(shared ? "Shared" : "Project")} · {(it.Current is InstanceBinding ? "Instance" : "Type")} · {type}"
                });
            }
            return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Reads the target, the document, the shared parameter file and every ticked element (base level included).</summary>
        public static FactsResult GatherFacts(Document doc, TargetRequest request)
        {
            var result = new FactsResult();
            TargetFacts t = result.Target;
            string name = (request.Name ?? string.Empty).Trim();
            t.Name = name;
            t.IsFamilyDocument = doc.IsFamilyDocument;
            t.IsReadOnly = doc.IsReadOnly;
            t.IsWorkshared = doc.IsWorkshared;

            var matches = FindBindings(doc, name);
            t.ProjectMatchCount = matches.Count;
            t.IsNew = request.Pending != null && matches.Count == 0 &&
                      string.Equals(request.Pending.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase);

            Definition def = null;
            ElementBinding binding = null;
            if (matches.Count == 1)
            {
                (def, binding) = matches[0];
                t.IsText = def.GetDataType() == SpecTypeId.String.Text;
                t.IsInstance = binding is InstanceBinding;
                if (def is InternalDefinition idef)
                {
                    t.VariesAcrossGroups = idef.VariesAcrossGroups;
                    if (doc.GetElement(idef.Id) is SharedParameterElement spe)
                    {
                        t.IsShared = true;
                        t.ProjectGuid = spe.GuidValue;
                    }
                }
            }
            else if (t.IsNew)
            {
                t.VariesAcrossGroups = true; // set on the new parameter when it is created
            }

            // Shared parameter file: the new parameter's file, else Revit's current one.
            t.FilePath = t.IsNew ? request.Pending.FilePath ?? string.Empty : doc.Application.SharedParametersFilename ?? string.Empty;
            ReadFileFacts(t);
            if (t.IsNew && t.FileEntry != null)
            {
                SharedParameterElement used = SharedParameterElement.Lookup(doc, t.FileEntry.Guid);
                if (used != null && !string.Equals(used.Name, t.Name, StringComparison.OrdinalIgnoreCase))
                    t.FileGuidUsedBy = used.Name;
            }

            // Elements.
            var categories = new Dictionary<long, Category>();
            var sampleByCategory = new Dictionary<long, Element>();
            int i = 0;
            foreach (long id in request.ElementIds)
            {
                ToolCancellation.CheckCurrent();
                ToolCancellation.ReportCurrent(++i, request.ElementIds.Count, $"Reading {i} of {request.ElementIds.Count}…");
                Element e = doc.GetElement(new ElementId(id));
                if (e == null || e.Category == null)
                {
                    result.Missing.Add(id);
                    continue;
                }
                long catId = e.Category.Id.Value;
                if (!categories.ContainsKey(catId))
                {
                    categories[catId] = e.Category;
                    sampleByCategory[catId] = e;
                }
                result.Elements.Add(ReadElement(doc, e, def, t));
            }

            t.CategoryCount = categories.Count;
            foreach (Category c in categories.Values.OrderBy(c => c.Name))
            {
                if (!c.AllowsBoundParameters) t.UnsupportedCategories.Add(c.Name);
                else if (binding != null && !binding.Categories.Contains(c)) t.MissingCategories.Add(c.Name);
            }

            // Built-in name clash, checked on one element per category.
            if (!string.IsNullOrEmpty(name))
            {
                foreach (var pair in sampleByCategory)
                {
                    foreach (Parameter p in pair.Value.GetParameters(name))
                    {
                        if (p.Definition is InternalDefinition d && d.BuiltInParameter != BuiltInParameter.INVALID)
                        {
                            t.BuiltInClashCategories.Add(categories[pair.Key].Name);
                            break;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Re-runs the pre-checks, then creates or extends the parameter and writes the values in one
        /// transaction. Per-element write failures are reported and the run goes on; any other error
        /// propagates, so the transaction rolls back and ToolGuard shows it.
        /// </summary>
        public static RunResult Run(Document doc, TargetRequest request)
        {
            var run = new RunResult();
            FactsResult facts = GatherFacts(doc, request);
            run.Facts = facts;
            TargetFacts t = facts.Target;

            var checks = BaseLevelRules.EvaluateChecks(t, facts.Elements, request.Overwrite, request.ClearOld, request.Choices, request.Format, request.CustomPattern);
            if (BaseLevelRules.AnyBlocked(checks))
            {
                run.NotStartedReason = "A pre-check is blocked: " +
                    string.Join(" ", checks.Where(c => c.Result == CheckResult.Blocked).Select(c => $"{c.Title}: {c.Detail}"));
                return run;
            }

            var ready = new List<ElementFacts>();
            foreach (ElementFacts e in facts.Elements)
            {
                RowVerdict v = BaseLevelRules.EvaluateRow(e, false, request.Overwrite, request.ChoiceFor(e.Id), request.ClearOld, t.VariesAcrossGroups,
                    e.HasLevel ? request.ValueFor(e) : null);
                if (v.State == RowState.Ready) ready.Add(e);
                else run.Rows.Add(new RunRowResult
                {
                    Id = e.Id,
                    State = RowState.Skipped,
                    Text = v.State == RowState.UpToDate ? "Skipped · up to date" : v.Text
                });
            }
            foreach (long id in facts.Missing)
                run.Rows.Add(new RunRowResult { Id = id, State = RowState.Skipped, Text = "Skipped · element no longer exists" });

            if (ready.Count == 0)
            {
                run.NotStartedReason = "No element is ready to assign; nothing was changed.";
                return run;
            }

            // The shared parameter file is written outside the transaction (a file cannot roll back).
            ExternalDefinition newDefinition = null;
            if (t.IsNew)
                newDefinition = EnsureFileDefinition(doc, request.Pending, run);

            using (var tx = new Transaction(doc, TransactionName))
            {
                tx.Start();

                InternalDefinition target = t.IsNew
                    ? BindNew(doc, newDefinition, facts, run)
                    : ExtendBinding(doc, t, request, run);

                int done = 0;
                var rewrites = new List<string>();
                var clears = new List<string>();
                foreach (ElementFacts e in ready)
                {
                    ToolCancellation.CheckCurrent();
                    ToolCancellation.ReportCurrent(++done, ready.Count, $"Assigning {done} of {ready.Count}…");
                    // No base level here means "Clear old values": write an empty value.
                    string value = e.HasLevel ? request.ValueFor(e) : string.Empty;
                    RunRowResult row = Assign(doc, e, target, value);
                    run.Rows.Add(row);
                    if (row.State == RowState.Assigned && !e.HasLevel)
                    {
                        row.Text = "Assigned · cleared value";
                        clears.Add($"{e.Id} “{e.CurrentValue}”");
                    }
                    else if (row.State == RowState.Assigned && !string.IsNullOrEmpty(e.CurrentValue))
                    {
                        row.Text = "Assigned · rewrote value";
                        rewrites.Add($"{e.Id} “{e.CurrentValue}” → “{value}”");
                    }
                }
                if (clears.Count > 0)
                    run.Log.Add((Shared.Models.LogLevel.Info, $"Cleared {clears.Count} old value(s) on elements with no base level: {string.Join(", ", clears)}."));
                if (rewrites.Count > 0)
                    run.Log.Add((Shared.Models.LogLevel.Info, $"Rewrote {rewrites.Count} existing value(s): {string.Join(", ", rewrites)}."));

                TransactionStatus status = tx.Commit();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException($"Revit did not commit the changes (status {status}). Nothing was changed.");
            }

            run.Committed = true;
            return run;
        }

        // ── element facts ─────────────────────────────────────────────────────
        private static ElementFacts ReadElement(Document doc, Element e, Definition def, TargetFacts t)
        {
            var f = new ElementFacts
            {
                Id = e.Id.Value,
                Category = e.Category.Name,
                InGroup = e.GroupId != ElementId.InvalidElementId,
                CategoryUnsupported = !e.Category.AllowsBoundParameters
            };

            BaseLevelInfo level = BaseLevelReader.Read(doc, e);
            f.HasLevel = level.Found;
            f.LevelName = level.LevelName;
            f.OffsetMm = level.OffsetMm;
            f.LevelSource = level.Source;

            if (def != null)
            {
                Parameter p = e.get_Parameter(def);
                if (p != null)
                {
                    f.CurrentValue = p.StorageType == StorageType.String ? p.AsString() ?? string.Empty : p.AsValueString() ?? string.Empty;
                    f.ParamReadOnly = p.IsReadOnly;
                }
            }

            if (t.IsWorkshared)
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, e.Id, out string owner);
                if (status == CheckoutStatus.OwnedByOtherUser) f.OwnedBy = string.IsNullOrEmpty(owner) ? "another user" : owner;
                ModelUpdatesStatus updates = WorksharingUtils.GetModelUpdatesStatus(doc, e.Id);
                f.OutOfDate = updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral;
            }
            return f;
        }

        private static void ReadFileFacts(TargetFacts t)
        {
            if (string.IsNullOrWhiteSpace(t.FilePath)) return;
            try
            {
                t.FileExists = File.Exists(t.FilePath);
                if (!t.FileExists) return;
                t.FileWritable = SharedParamFileReader.IsWritable(t.FilePath);
                var matches = SharedParamFileReader.FindAll(SharedParamFileReader.Read(t.FilePath), t.Name);
                t.FileMatchCount = matches.Count;
                t.FileEntry = matches.Count > 0 ? matches[0] : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                t.FileReadError = ex.Message;
            }
        }

        // ── run steps ─────────────────────────────────────────────────────────
        private static ExternalDefinition EnsureFileDefinition(Document doc, NewParameterSpec spec, RunResult run)
        {
            using var scope = new SharedParameterFileScope(doc.Application, spec.FilePath);
            foreach (DefinitionGroup g in scope.File.Groups)
            {
                foreach (Definition d in g.Definitions)
                {
                    if (d is ExternalDefinition ext && string.Equals(ext.Name, spec.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        run.Log.Add((Shared.Models.LogLevel.Info, $"“{ext.Name}” found in {Path.GetFileName(spec.FilePath)} (group “{g.Name}”); GUID {ext.GUID} reused."));
                        return ext;
                    }
                }
            }

            string groupName = string.IsNullOrWhiteSpace(spec.FileGroup) ? "Data" : spec.FileGroup.Trim();
            DefinitionGroup group = scope.File.Groups.get_Item(groupName) ?? scope.File.Groups.Create(groupName);
            var options = new ExternalDefinitionCreationOptions(spec.Name, SpecTypeId.String.Text)
            {
                Description = "Base level and base offset, written by " + ToolCatalog.BaseLevelToParameter.Name + ".",
                UserModifiable = true,
                Visible = true
            };
            var created = (ExternalDefinition)group.Definitions.Create(options);
            run.Log.Add((Shared.Models.LogLevel.Info, $"Added “{spec.Name}” (Text, GUID {created.GUID}) to {Path.GetFileName(spec.FilePath)} / {groupName}. The file keeps this entry even if the run is undone."));
            return created;
        }

        private static InternalDefinition BindNew(Document doc, ExternalDefinition definition, FactsResult facts, RunResult run)
        {
            CategorySet categories = BindableCategories(doc, facts);
            InstanceBinding binding = doc.Application.Create.NewInstanceBinding(categories);
            if (!doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Data))
                throw new InvalidOperationException($"Revit rejected the binding of “{definition.Name}”.");

            InternalDefinition target = FindBindings(doc, definition.Name).Select(m => m.Def).OfType<InternalDefinition>().FirstOrDefault()
                ?? throw new InvalidOperationException($"“{definition.Name}” was bound but cannot be found in the project.");
            target.SetAllowVaryBetweenGroups(doc, true);
            run.Log.Add((Shared.Models.LogLevel.Success, $"Created “{definition.Name}”: shared, instance, Text, Properties group Data, values can vary by group; bound to {categories.Size} categor{(categories.Size == 1 ? "y" : "ies")}."));
            return target;
        }

        private static InternalDefinition ExtendBinding(Document doc, TargetFacts t, TargetRequest request, RunResult run)
        {
            var (def, binding) = FindBindings(doc, t.Name).Single();
            if (t.MissingCategories.Count > 0)
            {
                foreach (Category c in BindableCategories(doc, request).Cast<Category>())
                    if (!binding.Categories.Contains(c)) binding.Categories.Insert(c);
                if (!doc.ParameterBindings.ReInsert(def, binding, def.GetGroupTypeId()))
                    throw new InvalidOperationException($"Revit rejected the new categories for “{def.Name}”.");
                run.Log.Add((Shared.Models.LogLevel.Info, $"Added {string.Join(", ", t.MissingCategories)} to the binding of “{def.Name}”."));
            }
            return def as InternalDefinition
                ?? throw new InvalidOperationException($"“{def.Name}” is not an internal definition.");
        }

        private static RunRowResult Assign(Document doc, ElementFacts f, InternalDefinition target, string value)
        {
            var row = new RunRowResult { Id = f.Id, Value = value };
            Element e = doc.GetElement(new ElementId(f.Id));
            Parameter p = e?.get_Parameter(target);
            if (p == null)
            {
                row.State = RowState.Failed;
                row.Text = "Failed · parameter not found on element";
                return row;
            }
            if (p.IsReadOnly)
            {
                row.State = RowState.Skipped;
                row.Text = "Skipped · parameter is read-only";
                return row;
            }
            try
            {
                if (p.Set(value))
                {
                    row.State = RowState.Assigned;
                    row.Text = "Assigned";
                }
                else
                {
                    row.State = RowState.Failed;
                    row.Text = "Failed · Revit refused the value";
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                row.State = RowState.Failed;
                row.Text = "Failed · " + ex.Message;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                row.State = RowState.Failed;
                row.Text = "Failed · " + ex.Message;
            }
            return row;
        }

        // ── helpers ───────────────────────────────────────────────────────────
        private static CategorySet BindableCategories(Document doc, FactsResult facts) =>
            BindableCategories(doc, facts.Elements.Select(e => e.Id));

        private static CategorySet BindableCategories(Document doc, TargetRequest request) =>
            BindableCategories(doc, request.ElementIds);

        private static CategorySet BindableCategories(Document doc, IEnumerable<long> ids)
        {
            var set = new CategorySet();
            var seen = new HashSet<long>();
            foreach (long id in ids)
            {
                Category c = doc.GetElement(new ElementId(id))?.Category;
                if (c == null || !c.AllowsBoundParameters || !seen.Add(c.Id.Value)) continue;
                set.Insert(c);
            }
            return set;
        }

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

        private static string SafeLabel(Definition def)
        {
            try
            {
                ForgeTypeId spec = def.GetDataType();
                return spec == SpecTypeId.String.Text ? "Text" : LabelUtils.GetLabelForSpec(spec);
            }
            catch
            {
                return "Other";
            }
        }
    }
}
