using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WorksetMemory.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WorksetMemory.V001.Core.Services
{
    /// <summary>
    /// Reads the open model's worksets, matches saved worksets to live ones, and loads / saves
    /// the memory file.
    /// </summary>
    public static class WorksetMemoryService
    {
        /// <summary>Folder under %AppData%\Revit26_Plugin_Revised.</summary>
        public const string ToolFolder = "WorksetMemory";

        /// <summary>Path of the memory file.</summary>
        public static string FilePath => JsonFileStore.PathFor(ToolFolder, "memory.json");

        /// <summary>Loads the memory file; <paramref name="warning"/> is set when it could not be read.</summary>
        public static WorksetMemoryFile Load(out string warning)
            => JsonFileStore.Load<WorksetMemoryFile>(FilePath, out warning);

        /// <summary>Saves the memory file. Throws on failure.</summary>
        public static void Save(WorksetMemoryFile file)
        {
            file.FormatVersion = WorksetMemoryFile.CurrentFormat;
            JsonFileStore.Save(FilePath, file);
        }

        /// <summary>Every user workset of <paramref name="doc"/>, sorted by name.</summary>
        public static List<SavedWorkset> ReadWorksets(Document doc)
        {
            return new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .Select(w => new SavedWorkset { Id = w.Id.IntegerValue, Name = w.Name, IsOpen = w.IsOpen })
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Snapshot of <paramref name="doc"/>'s open worksets and active workset, named <paramref name="name"/>.</summary>
        public static WorksetSet Capture(Document doc, string name)
        {
            WorksetTable table = doc.GetWorksetTable();
            WorksetId activeId = table.GetActiveWorksetId();
            Workset active = activeId == WorksetId.InvalidWorksetId ? null : table.GetWorkset(activeId);

            return new WorksetSet
            {
                Name = name,
                SavedAt = DateTime.Now,
                SavedBy = doc.Application.Username,
                ActiveWorksetId = active?.Id.IntegerValue ?? 0,
                ActiveWorksetName = active?.Name,
                Worksets = ReadWorksets(doc)
            };
        }

        /// <summary>
        /// Finds the live item for a saved workset: same name first (ignoring case), then same id.
        /// Returns null when the workset is gone.
        /// </summary>
        public static T Match<T>(IEnumerable<T> live, string name, int id, Func<T, string> nameOf, Func<T, int> idOf)
            where T : class
        {
            var list = live as IList<T> ?? live.ToList();
            return list.FirstOrDefault(l => string.Equals(nameOf(l), name, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(l => idOf(l) == id);
        }
    }
}
