using Revit26_Plugin_Revised.LinkMemory.V002.Core.Models;
using Revit26_Plugin_Revised.LinkMemory.V002.Core.Services;
using System.Windows;

namespace Revit26_Plugin_Revised.LinkMemory.V002.UI.ViewModels
{
    /// <summary>A saved memory in the list.</summary>
    public sealed class MemoryRow
    {
        /// <summary>Creates a row for <paramref name="memory"/>.</summary>
        public MemoryRow(LinkMemoryItem memory) => Memory = memory;

        /// <summary>The memory.</summary>
        public LinkMemoryItem Memory { get; }

        /// <summary>Memory name.</summary>
        public string Name => Memory.Name;

        /// <summary>"Print set", "View + related" or "Whole model (V001)".</summary>
        public string ScopeText => Memory.FromV001
            ? "Whole model (V001)"
            : LinkMemoryExporter.ScopeText(Memory.Scope);

        /// <summary>Save time and author.</summary>
        public string SavedText => string.IsNullOrEmpty(Memory.SavedBy)
            ? Memory.SavedAt.ToString("dd MMM yyyy · HH:mm")
            : $"{Memory.SavedAt:dd MMM yyyy · HH:mm} · {Memory.SavedBy}";

        /// <summary>"5 links".</summary>
        public string CountText => Memory.UsedCount == 1 ? "1 link" : $"{Memory.UsedCount} links";
    }

    /// <summary>One collected view in the list.</summary>
    public sealed class ViewRow
    {
        /// <summary>View name.</summary>
        public string Name { get; init; }

        /// <summary>View type.</summary>
        public string TypeText { get; init; }

        /// <summary>Why it was collected.</summary>
        public string Reason { get; init; }

        /// <summary>Links visible in it.</summary>
        public int LinkCount { get; init; }

        /// <summary>Indent level.</summary>
        public int Depth { get; init; }

        /// <summary>Left margin for the indent.</summary>
        public Thickness Indent => new(Depth * 16, 0, 0, 0);

        /// <summary>True for a root view (shown bold).</summary>
        public bool IsRoot => Depth == 0;
    }

    /// <summary>One line of the links grid.</summary>
    public sealed class LinkRow
    {
        /// <summary>Link name.</summary>
        public string Name { get; init; }

        /// <summary>Note after the name (checked out by, not in model…).</summary>
        public string Note { get; init; }

        /// <summary>Path, shown as a tooltip.</summary>
        public string Path { get; init; }

        /// <summary>Kind text.</summary>
        public string KindText { get; init; }

        /// <summary>"3 views", "—" or "unknown".</summary>
        public string ViewsText { get; init; }

        /// <summary>Status now.</summary>
        public string Now { get; init; }

        /// <summary>Whether the memory uses the link.</summary>
        public LinkUse Use { get; init; }

        /// <summary>True when the link is not in the memory (added after it was saved).</summary>
        public bool NotInMemory { get; init; }

        /// <summary>Planned change.</summary>
        public LinkAction Action { get; init; }

        /// <summary>True when the row is drawn dimmed (not used, or not in the memory).</summary>
        public bool IsDim => NotInMemory || Use != LinkUse.Used;

        /// <summary>Name with its note.</summary>
        public string Display => string.IsNullOrEmpty(Note) ? Name : $"{Name}  ({Note})";

        /// <summary>Text of <see cref="Action"/>.</summary>
        public string ActionText => NotInMemory ? "Not in memory" : Action switch
        {
            LinkAction.Load => "Loads",
            LinkAction.Unload => "Unloads",
            LinkAction.Reload => "Reloads",
            LinkAction.CantUnload => "Can't unload (Revit API)",
            LinkAction.Skipped => "Skipped",
            _ => Use == LinkUse.Unknown ? "Unknown – load and collect again" : "No change"
        };
    }
}
