using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Core;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Infrastructure
{
    /// <summary>
    /// The Revit-side work: create a parameter and bind it, list the shared parameters of a project,
    /// and write them to a shared parameter file. Every method runs on the Revit API thread.
    /// </summary>
    public static class SharedParamStudioService
    {
        /// <summary>Categories that accept parameters, sorted by name. Call on the Revit API thread.</summary>
        public static List<CategoryItem> GetBindableCategories(Document doc)
        {
            var list = new List<CategoryItem>();
            foreach (Category c in doc.Settings.Categories)
            {
                if (c != null && c.AllowsBoundParameters)
                    list.Add(new CategoryItem(c.Name, c.Id.Value));
            }
            return list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Names of every parameter bound in the project, for live name checks in the window.</summary>
        public static HashSet<string> GetProjectParameterNames(Document doc)
        {
            var names = new HashSet<string>();
            for (DefinitionBindingMapIterator it = ForwardIterator(doc); it.MoveNext();)
                names.Add(SpKeys.NameKey(it.Key.Name));
            return names;
        }

        /// <summary>
        /// Adds a new definition to the shared parameter file and binds it to the chosen categories in
        /// the project. The binding is one transaction, so a failure leaves the project unchanged; the
        /// file entry stays when binding fails, and the result says so.
        /// </summary>
        public static JobResult CreateAndAssign(Document doc, CreateParameterRequest r)
        {
            if (doc.IsFamilyDocument) return JobResult.Fail("Open a project; families are not supported.");
            if (doc.IsReadOnly) return JobResult.Fail("The project is read-only.");
            if (r.DataType == null || r.PaletteGroup == null) return JobResult.Fail("Pick a data type and a Properties group.");
            if (string.IsNullOrWhiteSpace(r.FilePath)) return JobResult.Fail("Choose the shared parameter file to write the new parameter to.");
            if (string.IsNullOrWhiteSpace(r.FileGroup)) return JobResult.Fail("Enter the group name inside the shared parameter file.");
            if (r.CategoryIds.Count == 0) return JobResult.Fail("Tick at least one category.");

            string badName = SpKeys.ValidateName(r.Name);
            if (badName != null) return JobResult.Fail(badName);

            for (DefinitionBindingMapIterator it = ForwardIterator(doc); it.MoveNext();)
            {
                if (SpKeys.NameKey(it.Key.Name) == SpKeys.NameKey(r.Name))
                    return JobResult.Fail($"The project already has a parameter named '{it.Key.Name}'.");
            }

            var result = new JobResult();

            if (File.Exists(r.FilePath))
            {
                var existing = SpFileReader.Read(r.FilePath);
                if (existing.Params.Any(p => SpKeys.NameKey(p.Name) == SpKeys.NameKey(r.Name)))
                    return JobResult.Fail($"'{r.Name}' is already defined in {Path.GetFileName(r.FilePath)}. Pick another name.");
            }
            else
            {
                SpFileWriter.Write(new SpFile(), r.FilePath, false);
                result.Add(LogLevel.Info, $"Created shared parameter file {r.FilePath}.");
            }

            ExternalDefinition definition;
            using (var scope = new SharedParameterFileScope(doc.Application, r.FilePath))
            {
                DefinitionGroup group = scope.File.Groups.get_Item(r.FileGroup) ?? scope.File.Groups.Create(r.FileGroup);
                var options = new ExternalDefinitionCreationOptions(r.Name, r.DataType.Spec)
                {
                    Description = r.Description ?? string.Empty,
                    UserModifiable = r.UserModifiable,
                    Visible = r.Visible
                };
                definition = (ExternalDefinition)group.Definitions.Create(options);
            }
            result.Add(LogLevel.Info, $"Defined '{r.Name}' ({r.DataType.Name}, GUID {definition.GUID}) in {Path.GetFileName(r.FilePath)} / {r.FileGroup}.");

            var categories = new CategorySet();
            foreach (long id in r.CategoryIds)
            {
                Category cat = Category.GetCategory(doc, new ElementId(id));
                if (cat == null || !cat.AllowsBoundParameters)
                {
                    result.Add(LogLevel.Warning, $"Category {id} cannot take parameters; skipped.");
                    continue;
                }
                categories.Insert(cat);
            }
            if (categories.IsEmpty)
            {
                result.Add(LogLevel.Error, "No usable category; the parameter was defined in the file but not bound.");
                return result;
            }

            using (var t = new Transaction(doc, "Shared Parameter Studio — create and assign"))
            {
                t.Start();
                try
                {
                    Binding binding = r.IsInstance
                        ? doc.Application.Create.NewInstanceBinding(categories)
                        : (Binding)doc.Application.Create.NewTypeBinding(categories);
                    if (!doc.ParameterBindings.Insert(definition, binding, r.PaletteGroup.GroupTypeId))
                        throw new InvalidOperationException("Revit rejected the binding.");
                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    result.Add(LogLevel.Error, $"Binding failed and was rolled back: {ex.Message}. The definition is in the file but not in the project.");
                    return result;
                }
            }

            result.Success = true;
            result.Add(LogLevel.Success, $"Bound '{r.Name}' as {(r.IsInstance ? "instance" : "type")} parameter to {categories.Size} categor{(categories.Size == 1 ? "y" : "ies")} (group: {r.PaletteGroup.Name}). Undo with Ctrl+Z.");
            return result;
        }

        /// <summary>
        /// Lists the shared parameters bound in the project. Project parameters that are not shared are
        /// counted in <paramref name="skippedProjectParameters"/>.
        /// </summary>
        public static List<ProjectSharedParameter> CollectProjectParameters(Document doc, out int skippedProjectParameters)
        {
            skippedProjectParameters = 0;
            var guidById = new Dictionary<ElementId, Guid>();
            foreach (SharedParameterElement e in new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)))
                guidById[e.Id] = e.GuidValue;

            var list = new List<ProjectSharedParameter>();
            for (DefinitionBindingMapIterator it = ForwardIterator(doc); it.MoveNext();)
            {
                Definition def = it.Key;
                Guid guid;
                if (def is InternalDefinition idef && guidById.TryGetValue(idef.Id, out var g)) guid = g;
                else if (def is ExternalDefinition ext) guid = ext.GUID;
                else { skippedProjectParameters++; continue; }

                var binding = it.Current as ElementBinding;
                var spec = def.GetDataType();
                list.Add(new ProjectSharedParameter
                {
                    Guid = guid,
                    Name = def.Name,
                    Spec = spec,
                    DataTypeLabel = SafeLabel(() => LabelUtils.GetLabelForSpec(spec), spec?.TypeId),
                    GroupLabel = SafeLabel(() => LabelUtils.GetLabelForGroup(def.GetGroupTypeId()), def.GetGroupTypeId()?.TypeId),
                    BindingKind = binding is InstanceBinding ? "Instance" : "Type",
                    Categories = binding == null ? string.Empty
                        : string.Join(", ", binding.Categories.Cast<Category>().Select(c => c.Name).OrderBy(n => n))
                });
            }
            return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Writes the given project parameters into a shared parameter file (created when missing),
        /// keeping their GUIDs. Parameters whose GUID or name is already in the file are skipped and reported.
        /// </summary>
        public static JobResult WriteToFile(Document doc, string path, string fileGroup, IReadOnlyList<ProjectSharedParameter> parameters)
        {
            if (string.IsNullOrWhiteSpace(path)) return JobResult.Fail("Choose the shared parameter file to write to.");
            if (string.IsNullOrWhiteSpace(fileGroup)) return JobResult.Fail("Enter the group name to put the parameters in.");
            if (parameters.Count == 0) return JobResult.Fail("Tick at least one parameter.");

            var result = new JobResult();
            var known = new HashSet<string>();
            var knownNames = new HashSet<string>();
            if (File.Exists(path))
            {
                foreach (var p in SpFileReader.Read(path).Params)
                {
                    known.Add(SpKeys.GuidKey(p.Guid));
                    knownNames.Add(SpKeys.NameKey(p.Name));
                }
                File.Copy(path, path + ".bak", true);
            }
            else
            {
                SpFileWriter.Write(new SpFile(), path, false);
                result.Add(LogLevel.Info, $"Created shared parameter file {path}.");
            }

            int added = 0;
            using (var scope = new SharedParameterFileScope(doc.Application, path))
            {
                DefinitionGroup group = scope.File.Groups.get_Item(fileGroup) ?? scope.File.Groups.Create(fileGroup);
                foreach (var p in parameters)
                {
                    if (known.Contains(SpKeys.GuidKey(p.Guid.ToString())))
                    {
                        result.Add(LogLevel.Warning, $"'{p.Name}' skipped: its GUID is already in the file.");
                        continue;
                    }
                    if (knownNames.Contains(SpKeys.NameKey(p.Name)))
                    {
                        result.Add(LogLevel.Warning, $"'{p.Name}' skipped: a parameter with that name is already in the file.");
                        continue;
                    }
                    try
                    {
                        var options = new ExternalDefinitionCreationOptions(p.Name, p.Spec) { GUID = p.Guid };
                        group.Definitions.Create(options);
                        known.Add(SpKeys.GuidKey(p.Guid.ToString()));
                        knownNames.Add(SpKeys.NameKey(p.Name));
                        added++;
                    }
                    catch (Exception ex)
                    {
                        result.Add(LogLevel.Error, $"'{p.Name}' failed: {ex.Message}");
                    }
                }
            }

            result.Success = added > 0;
            result.Add(added > 0 ? LogLevel.Success : LogLevel.Warning,
                $"Wrote {added} of {parameters.Count} parameter(s) to {Path.GetFileName(path)} / {fileGroup}.");
            return result;
        }

        private static DefinitionBindingMapIterator ForwardIterator(Document doc) => doc.ParameterBindings.ForwardIterator();

        private static string SafeLabel(Func<string> label, string fallback)
        {
            try { return label(); }
            catch { return fallback ?? string.Empty; }
        }
    }
}
