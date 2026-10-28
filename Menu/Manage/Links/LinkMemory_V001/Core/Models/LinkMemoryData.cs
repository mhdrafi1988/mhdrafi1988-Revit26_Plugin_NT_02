using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Revit26_Plugin_Revised.LinkMemory.V001.Core.Models
{
    /// <summary>
    /// Contents of %AppData%\Revit26_Plugin_Revised\LinkMemory\memory.json: every remembered
    /// model and its saved link sets.
    /// </summary>
    public sealed class LinkMemoryFile
    {
        /// <summary>Format this build writes. Bump and migrate when the shape changes.</summary>
        public const int CurrentFormat = 1;

        /// <summary>Format of the file as read; new files get <see cref="CurrentFormat"/>.</summary>
        public int FormatVersion { get; set; } = CurrentFormat;

        /// <summary>Remembered models.</summary>
        public List<LinkModelEntry> Models { get; set; } = new();

        /// <summary>The entry for <paramref name="key"/>, or null (also for an empty key).</summary>
        public LinkModelEntry Find(string key)
            => string.IsNullOrEmpty(key) ? null
                : Models.FirstOrDefault(m => string.Equals(m.Model?.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>The entry for <paramref name="model"/>, added when there is none yet.</summary>
        public LinkModelEntry Ensure(ModelIdentity model)
        {
            LinkModelEntry entry = Find(model.Key);
            if (entry != null) return entry;
            entry = new LinkModelEntry { Model = model };
            Models.Add(entry);
            return entry;
        }

        /// <summary>Replaces missing lists (a hand-edited or older file) with empty ones.</summary>
        public void Normalize()
        {
            Models ??= new List<LinkModelEntry>();
            Models.RemoveAll(m => m == null);
            foreach (LinkModelEntry entry in Models)
            {
                entry.Sets ??= new List<LinkSet>();
                entry.Sets.RemoveAll(s => s == null);
                foreach (LinkSet set in entry.Sets) set.Links ??= new List<SavedLink>();
            }
        }
    }

    /// <summary>One host model and its saved link sets.</summary>
    public sealed class LinkModelEntry
    {
        /// <summary>Which host model.</summary>
        public ModelIdentity Model { get; set; }

        /// <summary>Saved sets, newest first.</summary>
        public List<LinkSet> Sets { get; set; } = new();

        /// <summary>The set called <paramref name="name"/> (ignoring case), or null.</summary>
        public LinkSet FindSet(string name)
            => Sets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A named snapshot of which top-level Revit links were loaded.</summary>
    public sealed class LinkSet
    {
        /// <summary>Name the user gave the set.</summary>
        public string Name { get; set; }

        /// <summary>When the set was saved (local time).</summary>
        public DateTime SavedAt { get; set; }

        /// <summary>Revit user name of whoever saved it.</summary>
        public string SavedBy { get; set; }

        /// <summary>Every top-level Revit link type when saved.</summary>
        public List<SavedLink> Links { get; set; } = new();

        /// <summary>Number of links the set has loaded.</summary>
        [JsonIgnore] public int LoadedCount => Links.Count(l => l.IsLoaded);
    }

    /// <summary>One Revit link type as saved.</summary>
    public sealed class SavedLink
    {
        /// <summary>UniqueId of the RevitLinkType; matched first.</summary>
        public string UniqueId { get; set; }

        /// <summary>Link type name (usually the file name); matched when the UniqueId is gone.</summary>
        public string Name { get; set; }

        /// <summary>User-visible path of the linked file, for information.</summary>
        public string Path { get; set; }

        /// <summary>True when the link was loaded.</summary>
        public bool IsLoaded { get; set; }
    }
}
