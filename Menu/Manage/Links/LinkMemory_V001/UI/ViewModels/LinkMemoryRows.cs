using Revit26_Plugin_Revised.LinkMemory.V001.Core.Models;

namespace Revit26_Plugin_Revised.LinkMemory.V001.UI.ViewModels
{
    /// <summary>A saved set in the list.</summary>
    public sealed class LinkSetRow
    {
        /// <summary>Creates a row for <paramref name="set"/>.</summary>
        public LinkSetRow(LinkSet set) => Set = set;

        /// <summary>The saved set.</summary>
        public LinkSet Set { get; }

        /// <summary>Set name.</summary>
        public string Name => Set.Name;

        /// <summary>Save time and author.</summary>
        public string SavedText => string.IsNullOrEmpty(Set.SavedBy)
            ? Set.SavedAt.ToString("dd MMM yyyy · HH:mm")
            : $"{Set.SavedAt:dd MMM yyyy · HH:mm} · {Set.SavedBy}";

        /// <summary>"7 of 9 loaded".</summary>
        public string CountText => $"{Set.LoadedCount} of {Set.Links.Count} loaded";
    }

    /// <summary>What a reload does to one link.</summary>
    public enum LinkChange
    {
        /// <summary>No change.</summary>
        None,
        /// <summary>Unloaded now, loaded after reload.</summary>
        Loads,
        /// <summary>Loaded now, unloaded after reload.</summary>
        Unloads,
        /// <summary>Loaded now and reloaded from file.</summary>
        Reloads,
        /// <summary>Can't be changed (not in the model, closed workset, checked out by someone else).</summary>
        Skipped
    }

    /// <summary>One line of the comparison grid.</summary>
    public sealed class LinkRow
    {
        /// <summary>Link name.</summary>
        public string Name { get; init; }

        /// <summary>Extra note after the name (who has it checked out, not in model…).</summary>
        public string Note { get; init; }

        /// <summary>Path of the linked file, shown as a tooltip.</summary>
        public string Path { get; init; }

        /// <summary>"Loaded", "Unloaded", "Not found", "Closed workset" or "—".</summary>
        public string Now { get; init; }

        /// <summary>"Loaded", "Unloaded" or "Not in set".</summary>
        public string InSet { get; init; }

        /// <summary>Effect of a reload.</summary>
        public LinkChange Change { get; init; }

        /// <summary>Name with its note.</summary>
        public string Display => string.IsNullOrEmpty(Note) ? Name : $"{Name}  ({Note})";

        /// <summary>Label of <see cref="Change"/>.</summary>
        public string ChangeText => Change switch
        {
            LinkChange.Loads => Now == "Not found" ? "Loads (tries)" : "Loads",
            LinkChange.Unloads => "Unloads",
            LinkChange.Reloads => "Reloads",
            LinkChange.Skipped => "Skipped",
            _ => "—"
        };
    }
}
