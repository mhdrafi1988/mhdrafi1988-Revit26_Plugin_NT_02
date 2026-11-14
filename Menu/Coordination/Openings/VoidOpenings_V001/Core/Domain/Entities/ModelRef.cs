using System;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities
{
    /// <summary>
    /// Identifies a model taking part in a run: the host or one link instance. <see cref="ModelKey"/>
    /// is stable across sessions and machines (cloud GUIDs or central path), so stored openings can
    /// be matched to their source models later.
    /// </summary>
    public sealed class ModelRef
    {
        /// <summary>Stable key of the model.</summary>
        public string ModelKey { get; }

        /// <summary>Name shown to the user.</summary>
        public string DisplayName { get; }

        /// <summary>True for the active (host) model, false for a link.</summary>
        public bool IsHost { get; set; }

        /// <summary>True when the model's elements can be read (a link that is loaded, or the host).</summary>
        public bool IsLoaded { get; set; } = true;

        /// <summary>UniqueId of the link instance in the host; null for the host itself.</summary>
        public string LinkInstanceUniqueId { get; set; }

        /// <summary>Creates a model reference.</summary>
        public ModelRef(string modelKey, string displayName)
        {
            ModelKey = modelKey ?? throw new ArgumentNullException(nameof(modelKey));
            DisplayName = displayName ?? modelKey;
        }
    }
}
