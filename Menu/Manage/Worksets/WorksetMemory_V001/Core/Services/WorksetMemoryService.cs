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
        {
            WorksetMemoryFile file = JsonFileStore.Load<WorksetMemoryFile>(FilePath, out warning);
            file.Normalize();
            return file;
        }

        /// <summary>
        /// Re-reads the memory file, runs <paramref name="change"/> on the latest content and saves it
        /// when <paramref name="change"/> returns true, so sets another Revit session saved in the
        /// meantime are kept. Returns the file as saved. Throws <see cref="System.IO.IOException"/>,
        /// <see cref="UnauthorizedAccessException"/> or <see cref="NotSupportedException"/> on failure.
        /// </summary>
        public static WorksetMemoryFile Update(Func<WorksetMemoryFile, bool> change)
            => JsonFileStore.Update<WorksetMemoryFile>(FilePath, file =>
            {
                file.Normalize();
                if (!change(file)) return false;
                file.FormatVersion = WorksetMemoryFile.CurrentFormat;
                return true;
            });

        /// <summary>Loads the window options (defaults when there are none or they can't be read).</summary>
        public static WorksetMemorySettings LoadSettings()
            => SettingsService<WorksetMemorySettings>.Load(ToolFolder, WorksetMemorySettings.FileName);

        /// <summary>Saves the window options (a failure only means they are not remembered).</summary>
        public static void SaveSettings(WorksetMemorySettings settings)
        {
            settings.FormatVersion = WorksetMemorySettings.CurrentFormat;
            SettingsService<WorksetMemorySettings>.Save(ToolFolder, WorksetMemorySettings.FileName, settings);
        }

        /// <summary>
        /// True when <paramref name="saved"/> no longer describes where <paramref name="now"/> is
        /// (for example a new local copy), so the saved identity should be refreshed.
        /// </summary>
        public static bool LocationChanged(ModelIdentity saved, ModelIdentity now)
        {
            if (saved == null) return now != null;
            if (now == null) return false;
            static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
            return !Same(saved.OpenPath, now.OpenPath) || !Same(saved.CentralPath, now.CentralPath)
                || !Same(saved.Name, now.Name) || saved.IsCloud != now.IsCloud || !Same(saved.CloudRegion, now.CloudRegion)
                || !Same(saved.ProjectGuid, now.ProjectGuid) || !Same(saved.ModelGuid, now.ModelGuid);
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
