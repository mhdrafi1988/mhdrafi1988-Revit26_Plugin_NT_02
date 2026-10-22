using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WorksetMemory.V001.Core.Models;

namespace Revit26_Plugin_Revised.WorksetMemory.V001.UI.ViewModels
{
    /// <summary>A model in the model picker.</summary>
    public sealed class ModelChoice
    {
        /// <summary>Creates a choice for <paramref name="model"/>.</summary>
        public ModelChoice(ModelIdentity model, WorksetModelEntry entry, bool isCurrent)
        {
            Model = model;
            Entry = entry;
            IsCurrent = isCurrent;
        }

        /// <summary>Model identity.</summary>
        public ModelIdentity Model { get; }

        /// <summary>Saved data, or null when nothing is saved for this model yet.</summary>
        public WorksetModelEntry Entry { get; set; }

        /// <summary>True for the model that is active in Revit.</summary>
        public bool IsCurrent { get; }

        /// <summary>Text shown in the picker.</summary>
        public string Display => IsCurrent ? $"{Model.Name}  (open now)" : Model.Name;
    }

    /// <summary>A saved set in the list.</summary>
    public sealed class SetRow
    {
        /// <summary>Creates a row for <paramref name="set"/>.</summary>
        public SetRow(WorksetSet set) => Set = set;

        /// <summary>The saved set.</summary>
        public WorksetSet Set { get; }

        /// <summary>Set name.</summary>
        public string Name => Set.Name;

        /// <summary>Save time and author.</summary>
        public string SavedText => string.IsNullOrEmpty(Set.SavedBy)
            ? Set.SavedAt.ToString("dd MMM yyyy · HH:mm")
            : $"{Set.SavedAt:dd MMM yyyy · HH:mm} · {Set.SavedBy}";

        /// <summary>"11 of 38 open".</summary>
        public string CountText => $"{Set.OpenCount} of {Set.Worksets.Count} open";
    }

    /// <summary>What a reload does to one workset.</summary>
    public enum WorksetChange
    {
        /// <summary>No change.</summary>
        None,
        /// <summary>Closed now, open after reload.</summary>
        Opens,
        /// <summary>Open now, closed after reload.</summary>
        Closes,
        /// <summary>Open in the set but no longer in the model.</summary>
        Skipped
    }

    /// <summary>One line of the comparison grid.</summary>
    public sealed class WorksetRow
    {
        /// <summary>Workset name.</summary>
        public string Name { get; init; }

        /// <summary>"Open", "Closed" or "—".</summary>
        public string Now { get; init; }

        /// <summary>"Open", "Closed" or "Not in set".</summary>
        public string InSet { get; init; }

        /// <summary>Effect of a reload.</summary>
        public WorksetChange Change { get; init; }

        /// <summary>True for the set's active workset.</summary>
        public bool IsActive { get; init; }

        /// <summary>True when the workset is not in the model any more.</summary>
        public bool IsMissing { get; init; }

        /// <summary>Label of <see cref="Change"/> ("" for no change).</summary>
        public string ChangeText => Change switch
        {
            WorksetChange.Opens => "Opens",
            WorksetChange.Closes => "Closes",
            WorksetChange.Skipped => "Skipped",
            _ => "—"
        };

        /// <summary>Name with the active marker and missing note.</summary>
        public string Display => (IsActive ? "★ " : "") + Name + (IsActive ? "  (active)" : "") + (IsMissing ? "  (not in model)" : "");
    }
}
