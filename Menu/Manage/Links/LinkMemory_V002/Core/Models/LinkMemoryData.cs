using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Revit26_Plugin_Revised.LinkMemory.V002.Core.Models
{
    // Enums are stored as numbers in the memory file: never reorder or reuse a value.

    /// <summary>What a memory was collected from.</summary>
    public enum MemoryScope
    {
        /// <summary>Every link of the model; a link counts as used when it was loaded (V001 sets).</summary>
        WholeModel = 0,
        /// <summary>The active view and the views connected to it.</summary>
        ActiveView = 1,
        /// <summary>Every sheet and view of a saved print set (View/Sheet Set).</summary>
        PrintSet = 2
    }

    /// <summary>Kind of linked or imported file.</summary>
    public enum LinkKind
    {
        /// <summary>Revit link (.rvt).</summary>
        Revit = 0,
        /// <summary>IFC link (a Revit link whose file is .ifc).</summary>
        Ifc = 1,
        /// <summary>Linked CAD file (DWG, DXF, DGN…).</summary>
        CadLink = 2,
        /// <summary>Imported CAD file (no link to the file).</summary>
        CadImport = 3,
        /// <summary>Point cloud.</summary>
        PointCloud = 4,
        /// <summary>Raster image or PDF.</summary>
        Image = 5
    }

    /// <summary>Whether a memory uses a link.</summary>
    public enum LinkUse
    {
        /// <summary>Visible in at least one collected view (or loaded, for a whole-model memory).</summary>
        Used = 0,
        /// <summary>Not visible in any collected view.</summary>
        NotUsed = 1,
        /// <summary>Unloaded when collected, so its visibility could not be checked.</summary>
        Unknown = 2
    }

    /// <summary>
    /// Contents of %AppData%\Revit26_Plugin_Revised\LinkMemory\memory_v002.json.
    /// </summary>
    public sealed class LinkMemoryFile
    {
        /// <summary>Format this build writes. Bump and migrate when the shape changes.</summary>
        public const int CurrentFormat = 2;

        /// <summary>Format of the file as read; new files get <see cref="CurrentFormat"/>.</summary>
        public int FormatVersion { get; set; } = CurrentFormat;

        /// <summary>True once the Link Memory V001 sets have been imported (done once).</summary>
        public bool V001Imported { get; set; }

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
    }

    /// <summary>One host model and its memories.</summary>
    public sealed class LinkModelEntry
    {
        /// <summary>Which host model.</summary>
        public ModelIdentity Model { get; set; }

        /// <summary>Saved memories, newest first.</summary>
        public List<LinkMemoryItem> Memories { get; set; } = new();

        /// <summary>The memory called <paramref name="name"/> (ignoring case), or null.</summary>
        public LinkMemoryItem FindMemory(string name)
            => Memories.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What to collect.</summary>
    public sealed class CollectOptions
    {
        /// <summary>Follow section marks.</summary>
        public bool Sections { get; set; } = true;
        /// <summary>Follow elevation marks.</summary>
        public bool Elevations { get; set; } = true;
        /// <summary>Follow callouts.</summary>
        public bool Callouts { get; set; } = true;
        /// <summary>Follow references to drafting views.</summary>
        public bool DraftingViews { get; set; } = true;
        /// <summary>Print set: also collect the views placed on its sheets.</summary>
        public bool PlacedViews { get; set; } = true;

        /// <summary>List Revit links.</summary>
        public bool Revit { get; set; } = true;
        /// <summary>List CAD links and imports.</summary>
        public bool Cad { get; set; } = true;
        /// <summary>List IFC links.</summary>
        public bool Ifc { get; set; } = true;
        /// <summary>List point clouds.</summary>
        public bool PointClouds { get; set; } = true;
        /// <summary>List images and PDFs.</summary>
        public bool Images { get; set; } = true;

        /// <summary>True when links of <paramref name="kind"/> are listed.</summary>
        public bool Includes(LinkKind kind) => kind switch
        {
            LinkKind.Revit => Revit,
            LinkKind.Ifc => Ifc,
            LinkKind.CadLink or LinkKind.CadImport => Cad,
            LinkKind.PointCloud => PointClouds,
            LinkKind.Image => Images,
            _ => true
        };

        /// <summary>Copy of these options.</summary>
        public CollectOptions Clone() => (CollectOptions)MemberwiseClone();
    }

    /// <summary>
    /// A named memory: the views collected from a source (active view or print set) and the
    /// links those views use.
    /// </summary>
    public sealed class LinkMemoryItem
    {
        /// <summary>Name the user gave the memory.</summary>
        public string Name { get; set; }

        /// <summary>What it was collected from.</summary>
        public MemoryScope Scope { get; set; }

        /// <summary>UniqueId of the source view (ActiveView scope).</summary>
        public string SourceUniqueId { get; set; }

        /// <summary>Name of the source view or print set.</summary>
        public string SourceName { get; set; }

        /// <summary>Options used to collect it; Update collects again with the same ones.</summary>
        public CollectOptions Options { get; set; } = new();

        /// <summary>When it was saved (local time).</summary>
        public DateTime SavedAt { get; set; }

        /// <summary>Revit user name of whoever saved it.</summary>
        public string SavedBy { get; set; }

        /// <summary>True for a set imported from Link Memory V001.</summary>
        public bool FromV001 { get; set; }

        /// <summary>Collected views, in display order.</summary>
        public List<SavedView> Views { get; set; } = new();

        /// <summary>Links of the model (of the listed kinds) and whether the views use them.</summary>
        public List<SavedLink> Links { get; set; } = new();

        /// <summary>Number of links the memory uses.</summary>
        [JsonIgnore] public int UsedCount => Links.Count(l => l.Use == LinkUse.Used);
    }

    /// <summary>One collected view.</summary>
    public sealed class SavedView
    {
        /// <summary>UniqueId of the view or sheet.</summary>
        public string UniqueId { get; set; }

        /// <summary>View name (sheets: "number – name").</summary>
        public string Name { get; set; }

        /// <summary>View type shown in the list ("Floor plan", "Section", "Sheet"…).</summary>
        public string ViewType { get; set; }

        /// <summary>Number of the sheet the view is placed on, if any.</summary>
        public string SheetNumber { get; set; }

        /// <summary>Name of the sheet the view is placed on, if any.</summary>
        public string SheetName { get; set; }

        /// <summary>Why it was collected ("Active view", "Mark in Level 02", "On sheet A-101"…).</summary>
        public string Reason { get; set; }

        /// <summary>Indent level in the list (0 = root).</summary>
        public int Depth { get; set; }

        /// <summary><see cref="SavedLink.UniqueId"/> of every link visible in this view.</summary>
        public List<string> LinkIds { get; set; } = new();
    }

    /// <summary>One link as saved.</summary>
    public sealed class SavedLink
    {
        /// <summary>UniqueId of the link type; matched first.</summary>
        public string UniqueId { get; set; }

        /// <summary>Link type name (usually the file name); matched when the UniqueId is gone.</summary>
        public string Name { get; set; }

        /// <summary>Kind of link.</summary>
        public LinkKind Kind { get; set; }

        /// <summary>User-visible path of the linked file, for information.</summary>
        public string Path { get; set; }

        /// <summary>True when the link was loaded when collected.</summary>
        public bool WasLoaded { get; set; }

        /// <summary>Whether the memory uses the link.</summary>
        public LinkUse Use { get; set; }

        /// <summary>Number of collected views it is visible in.</summary>
        public int ViewCount { get; set; }
    }
}
