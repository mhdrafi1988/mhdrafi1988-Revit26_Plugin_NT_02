using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.Revit
{
    /// <summary>What <see cref="TrackerParameterService.Ensure"/> did.</summary>
    public sealed class ParameterSetupResult
    {
        /// <summary>Parameters added to the project.</summary>
        public List<string> Created { get; } = new();

        /// <summary>Existing parameters that were bound to one more category.</summary>
        public List<string> Extended { get; } = new();

        /// <summary>Notes that do not stop the run.</summary>
        public List<string> Warnings { get; } = new();

        /// <summary>Problems that stop the run; nothing may be written when there is one.</summary>
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Makes sure the Tracker parameters exist as instance parameters (Text; Tracker_Progress Number) bound to the given categories. A
    /// parameter that already exists (shared or project, whatever its GUID) is reused and only gets the
    /// categories added to its binding; a missing one is created from the tool's own shared parameter file
    /// with the fixed GUIDs in <see cref="TrackerParameters"/>. Nothing is ever renamed or removed.
    /// Must run inside an open transaction.
    /// </summary>
    public static class TrackerParameterService
    {
        /// <summary>Path of the shared parameter file the tool creates definitions in.</summary>
        public static string SharedParameterFilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin", "TrackerId", "Tracker_SharedParameters.txt");

        /// <summary>Ensures each parameter in <paramref name="names"/> is usable on every category in <paramref name="categories"/>.</summary>
        public static ParameterSetupResult Ensure(Document doc, IReadOnlyCollection<Category> categories, IEnumerable<string> names)
        {
            var result = new ParameterSetupResult();
            var app = doc.Application;
            var missing = new List<string>();
            if (categories == null || categories.Count == 0)
            {
                result.Errors.Add("No category to bind the Tracker parameters to.");
                return result;
            }

            foreach (string name in names.Distinct())
            {
                var (definition, binding) = FindBinding(doc, name);
                if (definition == null)
                {
                    missing.Add(name);
                    continue;
                }

                if (binding is not InstanceBinding)
                {
                    result.Errors.Add($"'{name}' exists as a type parameter. It must be an instance parameter, so each element can have its own value. Change it in Manage ▸ Project Parameters.");
                    continue;
                }

                // A number parameter may also have been made as Text by hand; both can be written.
                var type = definition.GetDataType();
                bool numberWanted = IsNumber(name);
                if (type != SpecTypeId.String.Text && !(numberWanted && type == SpecTypeId.Number))
                {
                    result.Errors.Add($"'{name}' exists but is not a {(numberWanted ? "Number or Text" : "Text")} parameter. Change it in Manage ▸ Project Parameters.");
                    continue;
                }

                var toAdd = categories.Where(c => !binding.Categories.Contains(c)).ToList();
                if (toAdd.Count > 0)
                {
                    var set = app.Create.NewCategorySet();
                    foreach (Category c in binding.Categories) set.Insert(c);
                    foreach (Category c in toAdd) set.Insert(c);

                    var group = (definition as InternalDefinition)?.GetGroupTypeId() ?? GroupTypeId.IdentityData;
                    if (doc.ParameterBindings.ReInsert(definition, app.Create.NewInstanceBinding(set), group))
                        result.Extended.Add(name);
                    else
                        result.Errors.Add($"Could not add {string.Join(", ", toAdd.Select(c => c.Name))} to the categories of '{name}'.");
                }

                AllowVaryInGroups(doc, definition as InternalDefinition, name, result);
            }

            if (missing.Count > 0) Create(doc, app, categories, missing, result);
            return result;
        }

        /// <summary>True when <paramref name="name"/> is created as a Number parameter.</summary>
        public static bool IsNumber(string name) => TrackerParameters.All.Any(p => p.Name == name && p.IsNumber);

        /// <summary>The categories the parameter named <paramref name="name"/> is bound to (empty when it is not in the project).</summary>
        public static List<Category> BoundCategories(Document doc, string name)
        {
            var (_, binding) = FindBinding(doc, name);
            return binding == null ? new List<Category>() : binding.Categories.Cast<Category>().ToList();
        }

        /// <summary>The project's definition and binding for the parameter named <paramref name="name"/>, or nulls.</summary>
        public static (Definition Definition, ElementBinding Binding) FindBinding(Document doc, string name)
        {
            var it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key is Definition def && def.Name == name)
                    return (def, it.Current as ElementBinding);
            }
            return (null, null);
        }

        private static void Create(Document doc, Application app, IReadOnlyCollection<Category> categories, List<string> names, ParameterSetupResult result)
        {
            string previous = app.SharedParametersFilename;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SharedParameterFilePath));
                if (!File.Exists(SharedParameterFilePath)) File.WriteAllText(SharedParameterFilePath, string.Empty);

                app.SharedParametersFilename = SharedParameterFilePath;
                DefinitionFile file = app.OpenSharedParameterFile();
                if (file == null)
                {
                    result.Errors.Add($"Could not open the shared parameter file {SharedParameterFilePath}.");
                    return;
                }

                DefinitionGroup group = file.Groups.get_Item(TrackerParameters.GroupName) ?? file.Groups.Create(TrackerParameters.GroupName);

                foreach (string name in names)
                {
                    var spec = TrackerParameters.All.First(p => p.Name == name);
                    var definition = group.Definitions.get_Item(name) as ExternalDefinition
                                     ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(name, spec.IsNumber ? SpecTypeId.Number : SpecTypeId.String.Text)
                                     {
                                         GUID = spec.Guid,
                                         Description = spec.Description,
                                         UserModifiable = true,
                                         Visible = true
                                     }) as ExternalDefinition;

                    if (definition == null)
                    {
                        result.Errors.Add($"Could not create the shared parameter '{name}'.");
                        continue;
                    }
                    if (definition.GUID != spec.Guid)
                        result.Warnings.Add($"'{name}' in {SharedParameterFilePath} has GUID {definition.GUID}, not {spec.Guid}. It was used as found.");

                    var set = app.Create.NewCategorySet();
                    foreach (Category c in categories) set.Insert(c);
                    if (!doc.ParameterBindings.Insert(definition, app.Create.NewInstanceBinding(set), GroupTypeId.IdentityData))
                    {
                        result.Errors.Add($"Could not add the parameter '{name}' to the project.");
                        continue;
                    }

                    result.Created.Add(name);
                    AllowVaryInGroups(doc, FindBinding(doc, name).Definition as InternalDefinition, name, result);
                }
            }
            finally
            {
                try { app.SharedParametersFilename = previous ?? string.Empty; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* the user's file setting stays as Revit left it */ }
            }
        }

        // Elements inside groups need their own value; without this Revit would push one value to every copy of the group.
        private static void AllowVaryInGroups(Document doc, InternalDefinition definition, string name, ParameterSetupResult result)
        {
            if (definition == null || definition.VariesAcrossGroups) return;
            try
            {
                definition.SetAllowVaryBetweenGroups(doc, true);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Warnings.Add($"'{name}' cannot vary between group instances ({ex.Message}). Elements inside groups may be skipped.");
            }
        }
    }
}
