using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Matching;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Sections
{
    /// <summary>Decides what a model element is, for the section targets.</summary>
    public static class ItemClassifier
    {
        /// <summary>Category name used for doors.</summary>
        public const string Doors = "Doors";
        /// <summary>Category name used for plumbing fixtures.</summary>
        public const string PlumbingFixtures = "Plumbing Fixtures";
        /// <summary>Category name used for pipe accessories.</summary>
        public const string PipeAccessories = "Pipe Accessories";

        /// <summary>The categories searched, in the order they are listed.</summary>
        public static IReadOnlyList<string> Categories { get; } = new[] { Doors, PlumbingFixtures, PipeAccessories };

        /// <summary>
        /// Door, Drain, Wc or Shower for an element of <paramref name="category"/> called
        /// <paramref name="name"/> ("Family : Type"); null when it is none of them. Every door is a
        /// door; fixtures and accessories go by the keyword lists, drains first.
        /// </summary>
        public static SectionTargetKind? Classify(string category, string name, WetAreaSettings s)
        {
            if (category == Doors) return SectionTargetKind.Door;
            if (category != PlumbingFixtures && category != PipeAccessories) return null;
            if (string.IsNullOrWhiteSpace(name) || s == null) return null;
            if (KeywordMatcher.Match(name, s.DrainKeywords) != null) return SectionTargetKind.Drain;
            if (KeywordMatcher.Match(name, s.WcKeywords) != null) return SectionTargetKind.Wc;
            if (KeywordMatcher.Match(name, s.ShowerKeywords) != null) return SectionTargetKind.Shower;
            return null;
        }
    }

    /// <summary>Picks the doors, drains and fixtures that belong to a room.</summary>
    public static class RoomItemMatcher
    {
        /// <summary>A door may sit this far outside the room's outline (it is in the wall), mm.</summary>
        public const double DoorReachMm = 400;
        /// <summary>Items this far below the room's level still count (a drain set into the slab), mm.</summary>
        public const double BelowLevelMm = 500;
        /// <summary>Items up to this far above the room's level count, mm.</summary>
        public const double AboveLevelMm = 3000;

        /// <summary>
        /// Items of the room with outline <paramref name="outer"/> on level <paramref name="levelMm"/>:
        /// those inside the outline, and doors within <see cref="DoorReachMm"/> of it. A door between
        /// two rooms belongs to both.
        /// </summary>
        public static IReadOnlyList<FoundItem> ForRoom(IReadOnlyList<Pt> outer, double levelMm, IEnumerable<FoundItem> items)
        {
            if (outer == null || outer.Count < 3 || items == null) return new List<FoundItem>();
            return items.Where(i => i != null
                && i.ZMm >= levelMm - BelowLevelMm && i.ZMm <= levelMm + AboveLevelMm
                && (Polyline2D.Contains(outer, i.At)
                    || (i.Kind == SectionTargetKind.Door && Polyline2D.Nearest(outer, i.At).Distance <= DoorReachMm)))
                .ToList();
        }
    }
}
