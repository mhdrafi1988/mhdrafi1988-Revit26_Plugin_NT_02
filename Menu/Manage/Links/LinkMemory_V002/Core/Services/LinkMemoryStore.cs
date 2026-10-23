using Revit26_Plugin_Revised.LinkMemory.V002.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Revit26_Plugin_Revised.LinkMemory.V002.Core.Services
{
    /// <summary>
    /// Loads and saves the V002 memory file, and imports the Link Memory V001 sets into it once.
    /// The V001 file is only read, never changed.
    /// </summary>
    public static class LinkMemoryStore
    {
        /// <summary>Folder under %AppData%\Revit26_Plugin_Revised (shared with V001).</summary>
        public const string ToolFolder = "LinkMemory";

        /// <summary>Path of the V002 memory file.</summary>
        public static string FilePath => JsonFileStore.PathFor(ToolFolder, "memory_v002.json");

        /// <summary>Path of the V001 memory file.</summary>
        public static string V001FilePath => JsonFileStore.PathFor(ToolFolder, "memory.json");

        /// <summary>Loads the memory file; <paramref name="warning"/> is set when it could not be read.</summary>
        public static LinkMemoryFile Load(out string warning)
        {
            LinkMemoryFile file = JsonFileStore.Load<LinkMemoryFile>(FilePath, out warning);
            file.Models ??= new List<LinkModelEntry>();
            foreach (LinkModelEntry entry in file.Models)
            {
                entry.Memories ??= new List<LinkMemoryItem>();
                foreach (LinkMemoryItem m in entry.Memories)
                {
                    m.Options ??= new CollectOptions();
                    m.Views ??= new List<SavedView>();
                    m.Links ??= new List<SavedLink>();
                }
            }
            return file;
        }

        /// <summary>Saves the memory file. Throws on failure.</summary>
        public static void Save(LinkMemoryFile file)
        {
            file.FormatVersion = LinkMemoryFile.CurrentFormat;
            JsonFileStore.Save(FilePath, file);
        }

        /// <summary>
        /// Adds every V001 set to <paramref name="file"/> as a whole-model memory, the first time only.
        /// Returns the number imported; <paramref name="warning"/> is set when the V001 file could not
        /// be read (the import is then tried again next time).
        /// </summary>
        public static int ImportV001(LinkMemoryFile file, out string warning)
        {
            warning = null;
            if (file.V001Imported) return 0;

            string path = V001FilePath;
            if (!File.Exists(path))
            {
                file.V001Imported = true;
                return 0;
            }

            V1File old;
            try
            {
                old = JsonSerializer.Deserialize<V1File>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException
                                       || ex is NotSupportedException)
            {
                warning = $"Could not read the Link Memory V001 sets ({ex.Message}). They will be imported next time.";
                return 0;
            }

            int count = 0;
            foreach (V1Model model in old?.Models ?? new List<V1Model>())
            {
                if (model?.Model?.Key == null) continue;
                LinkModelEntry entry = file.Find(model.Model.Key);
                if (entry == null)
                {
                    entry = new LinkModelEntry { Model = model.Model };
                    file.Models.Add(entry);
                }

                foreach (V1Set set in model.Sets ?? new List<V1Set>())
                {
                    if (set == null || string.IsNullOrWhiteSpace(set.Name)) continue;
                    if (entry.Memories.Any(m => string.Equals(m.Name, set.Name, StringComparison.OrdinalIgnoreCase))) continue;

                    entry.Memories.Add(new LinkMemoryItem
                    {
                        Name = set.Name,
                        Scope = MemoryScope.WholeModel,
                        SourceName = "Whole model",
                        SavedAt = set.SavedAt,
                        SavedBy = set.SavedBy,
                        FromV001 = true,
                        Links = (set.Links ?? new List<V1Link>()).Where(l => l != null).Select(l => new SavedLink
                        {
                            UniqueId = l.UniqueId,
                            Name = l.Name,
                            Path = l.Path,
                            Kind = LinkCatalog.IsIfcPath(l.Path) || LinkCatalog.IsIfcPath(l.Name) ? LinkKind.Ifc : LinkKind.Revit,
                            WasLoaded = l.IsLoaded,
                            Use = l.IsLoaded ? LinkUse.Used : LinkUse.NotUsed
                        }).ToList()
                    });
                    count++;
                }
                entry.Memories = entry.Memories.OrderByDescending(m => m.SavedAt).ToList();
            }

            file.V001Imported = true;
            return count;
        }

        // Shape of the V001 memory.json (format 1), read without depending on the V001 code.
        private sealed class V1File
        {
            public List<V1Model> Models { get; set; }
        }

        private sealed class V1Model
        {
            public ModelIdentity Model { get; set; }
            public List<V1Set> Sets { get; set; }
        }

        private sealed class V1Set
        {
            public string Name { get; set; }
            public DateTime SavedAt { get; set; }
            public string SavedBy { get; set; }
            public List<V1Link> Links { get; set; }
        }

        private sealed class V1Link
        {
            public string UniqueId { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
            public bool IsLoaded { get; set; }
        }
    }
}
