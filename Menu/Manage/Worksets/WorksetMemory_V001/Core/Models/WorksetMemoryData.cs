using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Revit26_Plugin_Revised.WorksetMemory.V001.Core.Models
{
    /// <summary>
    /// Contents of %AppData%\Revit26_Plugin_Revised\WorksetMemory\memory.json: every remembered
    /// model and its saved workset sets.
    /// </summary>
    public sealed class WorksetMemoryFile
    {
        /// <summary>Format this build writes. Bump and migrate when the shape changes.</summary>
        public const int CurrentFormat = 1;

        /// <summary>Format of the file as read; new files get <see cref="CurrentFormat"/>.</summary>
        public int FormatVersion { get; set; } = CurrentFormat;

        /// <summary>Remembered models.</summary>
        public List<WorksetModelEntry> Models { get; set; } = new();

        /// <summary>The entry for <paramref name="key"/>, or null.</summary>
        public WorksetModelEntry Find(string key)
            => Models.FirstOrDefault(m => string.Equals(m.Model?.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One model and its saved workset sets.</summary>
    public sealed class WorksetModelEntry
    {
        /// <summary>Which model, and how to open it again.</summary>
        public ModelIdentity Model { get; set; }

        /// <summary>Saved sets, newest first.</summary>
        public List<WorksetSet> Sets { get; set; } = new();
    }

    /// <summary>A named snapshot of which user worksets were open, and which one was active.</summary>
    public sealed class WorksetSet
    {
        /// <summary>Name the user gave the set.</summary>
        public string Name { get; set; }

        /// <summary>When the set was saved (local time).</summary>
        public DateTime SavedAt { get; set; }

        /// <summary>Revit user name of whoever saved it.</summary>
        public string SavedBy { get; set; }

        /// <summary>Name of the active workset when saved.</summary>
        public string ActiveWorksetName { get; set; }

        /// <summary>Id of the active workset when saved.</summary>
        public int ActiveWorksetId { get; set; }

        /// <summary>Every user workset of the model when saved.</summary>
        public List<SavedWorkset> Worksets { get; set; } = new();

        /// <summary>Number of worksets the set has open.</summary>
        [JsonIgnore] public int OpenCount => Worksets.Count(w => w.IsOpen);
    }

    /// <summary>One user workset as saved.</summary>
    public sealed class SavedWorkset
    {
        /// <summary>WorksetId integer value.</summary>
        public int Id { get; set; }

        /// <summary>Workset name; matched first when the set is applied.</summary>
        public string Name { get; set; }

        /// <summary>True when the workset was open.</summary>
        public bool IsOpen { get; set; }
    }
}
