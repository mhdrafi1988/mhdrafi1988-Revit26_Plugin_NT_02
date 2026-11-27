using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models
{
    /// <summary>What a run does with rooms that already have waterproofing from an earlier run.</summary>
    public enum ReRunMode
    {
        /// <summary>Replace the old roof with a new one for the same room.</summary>
        UpdateInPlace = 0
    }

    /// <summary>What to do when no slab is found under a room.</summary>
    public enum NoSlabMode
    {
        /// <summary>Place the roof at the room's level plus the offset.</summary>
        LevelPlusOffset = 0
    }

    /// <summary>State of one room in the grid and in the report.</summary>
    public enum RoomStatus
    {
        /// <summary>Clean outline, nothing to report.</summary>
        Ready,
        /// <summary>The outline needed a repair and is now closed.</summary>
        Repaired,
        /// <summary>The outline's area differs from the room area by more than the tolerance.</summary>
        AreaWarning,
        /// <summary>The outline is an approximation (hull), not the real boundary.</summary>
        Approximate,
        /// <summary>No slab under the room; the level plus offset is used.</summary>
        NoSlab,
        /// <summary>The room or its slab is checked out by another user.</summary>
        Owned,
        /// <summary>The room is not placed or not enclosed.</summary>
        NotEnclosed,
        /// <summary>A roof from an earlier run exists for this room.</summary>
        AlreadyDone,
        /// <summary>No closed outline could be built.</summary>
        Failed,
        /// <summary>Not a wet room: its outline and slab are read when it is run.</summary>
        NotChecked
    }

    /// <summary>How a section target is placed on the room's plan. Values are stored in settings: append only.</summary>
    public enum SectionMode
    {
        /// <summary>A reference section pointing to a drafting view.</summary>
        ReferenceSection = 0,
        /// <summary>A callout that creates a cropped detail view of the model.</summary>
        Callout = 1,
        /// <summary>A section that creates a new section view of the model (tool 2.1.0).</summary>
        Section = 2,
        /// <summary>A reference callout pointing to a drafting view (tool 2.1.0).</summary>
        ReferenceCallout = 3
    }

    /// <summary>What each <see cref="SectionMode"/> means for the drafting view and the views it makes.</summary>
    public static class SectionModes
    {
        /// <summary>True for the modes that point to a drafting view (reference section, reference callout).</summary>
        public static bool UsesDraftingView(SectionMode mode) => mode is SectionMode.ReferenceSection or SectionMode.ReferenceCallout;

        /// <summary>Text shown in the Mode list, in this order.</summary>
        public static IReadOnlyList<string> Labels { get; } = new[] { "Ref. Section", "Callout", "Section", "Ref. Callout" };

        /// <summary>The Mode list text of <paramref name="mode"/>.</summary>
        public static string Label(SectionMode mode) => (int)mode >= 0 && (int)mode < Labels.Count ? Labels[(int)mode] : Labels[0];

        /// <summary>The mode for a Mode list text; reference section when unknown.</summary>
        public static SectionMode FromLabel(string label)
        {
            int i = Labels.ToList().IndexOf(label ?? string.Empty);
            return i < 0 ? SectionMode.ReferenceSection : (SectionMode)i;
        }
    }
}
