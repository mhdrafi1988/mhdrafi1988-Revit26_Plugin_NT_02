using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers
{
    /// <summary>
    /// Annotation kinds the priority order works with. Stored by name in settings.json, so never
    /// rename a member; add new ones at the end.
    /// </summary>
    public enum AnnotationCategory
    {
        /// <summary>Linear, angular, radial and spot dimensions.</summary>
        Dimension = 0,

        /// <summary>Room, space and area tags.</summary>
        RoomTag = 1,

        /// <summary>Door and window tags.</summary>
        DoorWindowTag = 2,

        /// <summary>Every other element tag (walls, furniture, equipment, materials, ...).</summary>
        OtherTag = 3,

        /// <summary>Keynote tags.</summary>
        KeynoteTag = 4,

        /// <summary>Text notes.</summary>
        TextNote = 5
    }

    /// <summary>Built-in priority orders the user can start from.</summary>
    public enum PriorityPreset
    {
        /// <summary>Architectural plan (recommended): dimensions stay, text notes move first.</summary>
        Architectural = 0,

        /// <summary>Text notes stay: for presentation sheets where notes are placed by hand.</summary>
        NotesStay = 1,

        /// <summary>Tags stay: for door / window / room schedules review.</summary>
        TagsStay = 2,

        /// <summary>The user's own order.</summary>
        Custom = 3
    }

    /// <summary>What happens when both elements of a pair have the same priority.</summary>
    public enum TieRule
    {
        /// <summary>Both move apart, half each.</summary>
        Distribute = 0,

        /// <summary>The upper element moves up.</summary>
        MoveUp = 1,

        /// <summary>The lower element moves down.</summary>
        MoveDown = 2,

        /// <summary>Leave the pair alone (reported as Skipped).</summary>
        Skip = 3
    }

    /// <summary>
    /// Priority order for resolving clashes: a higher-priority annotation stays put and the
    /// lower-priority one moves out of its way. Pure data, no Revit API (unit-tested).
    /// <para>
    /// The recommended order follows common architectural drafting practice: there is no standard
    /// that ranks annotations in a collision, but practice agrees on moving the most flexible note and
    /// keeping what is tied to geometry. Dimensions are tied to references and their strings; room tags
    /// identify spaces and anchor the finish schedule; door and window tags link openings to their
    /// schedules and must stay next to the opening; other tags are less critical; keynotes and text
    /// notes have leaders and can move furthest without losing their meaning.
    /// </para>
    /// </summary>
    public static class AnnotationPriority
    {
        /// <summary>Recommended order (highest priority first).</summary>
        public static readonly IReadOnlyList<AnnotationCategory> ArchitecturalOrder = new[]
        {
            AnnotationCategory.Dimension,
            AnnotationCategory.RoomTag,
            AnnotationCategory.DoorWindowTag,
            AnnotationCategory.OtherTag,
            AnnotationCategory.KeynoteTag,
            AnnotationCategory.TextNote
        };

        private static readonly IReadOnlyList<AnnotationCategory> NotesStayOrder = new[]
        {
            AnnotationCategory.TextNote,
            AnnotationCategory.KeynoteTag,
            AnnotationCategory.Dimension,
            AnnotationCategory.RoomTag,
            AnnotationCategory.DoorWindowTag,
            AnnotationCategory.OtherTag
        };

        private static readonly IReadOnlyList<AnnotationCategory> TagsStayOrder = new[]
        {
            AnnotationCategory.DoorWindowTag,
            AnnotationCategory.RoomTag,
            AnnotationCategory.OtherTag,
            AnnotationCategory.KeynoteTag,
            AnnotationCategory.Dimension,
            AnnotationCategory.TextNote
        };

        /// <summary>Every category, in enum order.</summary>
        public static IReadOnlyList<AnnotationCategory> AllCategories { get; } =
            (AnnotationCategory[])Enum.GetValues(typeof(AnnotationCategory));

        /// <summary>The order of <paramref name="preset"/>; Custom returns the recommended order as a start.</summary>
        public static IReadOnlyList<AnnotationCategory> OrderFor(PriorityPreset preset) => preset switch
        {
            PriorityPreset.NotesStay => NotesStayOrder,
            PriorityPreset.TagsStay => TagsStayOrder,
            _ => ArchitecturalOrder
        };

        /// <summary>The preset whose order equals <paramref name="order"/>, or Custom.</summary>
        public static PriorityPreset MatchPreset(IEnumerable<AnnotationCategory> order)
        {
            var list = order?.ToList() ?? new List<AnnotationCategory>();
            foreach (var p in new[] { PriorityPreset.Architectural, PriorityPreset.NotesStay, PriorityPreset.TagsStay })
                if (OrderFor(p).SequenceEqual(list))
                    return p;
            return PriorityPreset.Custom;
        }

        /// <summary>
        /// Makes a complete order from a stored list: unknown names are dropped, duplicates removed,
        /// and missing categories appended in recommended order. Never throws.
        /// </summary>
        public static List<AnnotationCategory> Normalize(IEnumerable<string> stored)
        {
            var result = new List<AnnotationCategory>();
            foreach (string name in stored ?? Enumerable.Empty<string>())
            {
                if (Enum.TryParse(name, out AnnotationCategory c) && Enum.IsDefined(typeof(AnnotationCategory), c) && !result.Contains(c))
                    result.Add(c);
            }
            foreach (var c in ArchitecturalOrder)
                if (!result.Contains(c))
                    result.Add(c);
            return result;
        }

        /// <summary>Label shown in the window.</summary>
        public static string Label(AnnotationCategory c) => c switch
        {
            AnnotationCategory.Dimension => "Dimensions",
            AnnotationCategory.RoomTag => "Room / space / area tags",
            AnnotationCategory.DoorWindowTag => "Door & window tags",
            AnnotationCategory.OtherTag => "Other element tags",
            AnnotationCategory.KeynoteTag => "Keynote tags",
            AnnotationCategory.TextNote => "Text notes",
            _ => c.ToString()
        };

        /// <summary>One-line reason shown next to each row of the recommended order.</summary>
        public static string Reason(AnnotationCategory c) => c switch
        {
            AnnotationCategory.Dimension => "Tied to geometry and dimension strings; moving them changes what they measure.",
            AnnotationCategory.RoomTag => "Identify the space and link it to the finish schedule.",
            AnnotationCategory.DoorWindowTag => "Link openings to their schedules; must stay next to the opening.",
            AnnotationCategory.OtherTag => "Usually have a leader and some room to move.",
            AnnotationCategory.KeynoteTag => "Referenced to a legend; can move along their leader.",
            AnnotationCategory.TextNote => "Most flexible: free text with an optional leader.",
            _ => string.Empty
        };

        /// <summary>Display name of a preset.</summary>
        public static string PresetLabel(PriorityPreset p) => p switch
        {
            PriorityPreset.Architectural => "Architectural plan (recommended)",
            PriorityPreset.NotesStay => "Text notes stay (presentation sheets)",
            PriorityPreset.TagsStay => "Tags stay (schedule review)",
            _ => "Custom"
        };
    }
}
