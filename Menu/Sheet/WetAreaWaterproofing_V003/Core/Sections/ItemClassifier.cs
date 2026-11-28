using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Sections
{
    /// <summary>Decides which Mapping rows an element belongs to (tool 3.0.0; V002 used fixed keyword lists).</summary>
    public static class ItemClassifier
    {
        /// <summary>
        /// The rows an element of <paramref name="category"/> called <paramref name="typeName"/> ("Family : Type")
        /// belongs to, with the target kind each row gives it (Door for a door row cut at the threshold, else Element).
        /// Rows of the Room pseudo category never match an element.
        /// </summary>
        public static IEnumerable<(MappingRule Rule, SectionTargetKind Kind)> Classify(string category, string typeName, IEnumerable<MappingRule> rules)
        {
            foreach (MappingRule r in rules ?? Enumerable.Empty<MappingRule>())
                if (MappingRules.Matches(r, category, typeName))
                    yield return (r, r.Category == MappingRules.Doors && r.Cut == MappingCut.ThroughElement ? SectionTargetKind.Door : SectionTargetKind.Element);
        }
    }

    /// <summary>Picks the doors, walls, fixtures and other mapped elements that belong to a room.</summary>
    public static class RoomItemMatcher
    {
        /// <summary>A door may sit this far outside the room's outline (it is in the wall), mm.</summary>
        public const double DoorReachMm = 400;
        /// <summary>Items this far below the room's level still count (a drain set into the slab), mm.</summary>
        public const double BelowLevelMm = 500;
        /// <summary>Items up to this far above the room's level count, mm; less when the next level is closer.</summary>
        public const double AboveLevelMm = 2000;
        /// <summary>A room edge lies along a wall when its middle is within half the wall's thickness plus this, mm.</summary>
        public const double WallReachMm = 150;

        /// <summary>
        /// Items of the room with outline <paramref name="outer"/> on level <paramref name="levelMm"/>:
        /// those inside the outline, doors within <see cref="DoorReachMm"/> of it, and walls along one of its
        /// edges. A door between two rooms belongs to both. The height band runs from <see cref="BelowLevelMm"/>
        /// under the level to <see cref="AboveLevelMm"/> over it, and stops 500 mm short of
        /// <paramref name="nextLevelMm"/> (the level above) so the floor above is never counted. A wall counts
        /// when its base is in that band.
        /// </summary>
        public static IReadOnlyList<FoundItem> ForRoom(IReadOnlyList<Pt> outer, double levelMm, IEnumerable<FoundItem> items, double? nextLevelMm = null)
        {
            if (outer == null || outer.Count < 3 || items == null) return new List<FoundItem>();
            double top = levelMm + AboveLevelMm;
            if (nextLevelMm.HasValue && nextLevelMm.Value > levelMm) top = Math.Min(top, nextLevelMm.Value - BelowLevelMm);
            return items.Where(i => i != null
                && i.ZMm >= levelMm - BelowLevelMm && i.ZMm <= top
                && (i.IsLine
                    ? Enumerable.Range(0, outer.Count).Any(e => EdgeAlongWall(outer, e, i))
                    : Polyline2D.Contains(outer, i.At)
                      || (i.Kind == SectionTargetKind.Door && Polyline2D.Nearest(outer, i.At).Distance <= DoorReachMm)))
                .ToList();
        }

        /// <summary>True when edge <paramref name="edge"/> of <paramref name="outer"/> runs along the wall <paramref name="wall"/>.</summary>
        public static bool EdgeAlongWall(IReadOnlyList<Pt> outer, int edge, FoundItem wall)
        {
            if (wall == null || !wall.IsLine || outer == null || outer.Count < 2) return false;
            Pt a = outer[edge], b = outer[(edge + 1) % outer.Count];
            return DistanceToSegment(a.MidTo(b), wall.LineStart, wall.LineEnd) <= wall.HalfWidthMm + WallReachMm;
        }

        /// <summary>Distance from <paramref name="p"/> to the segment <paramref name="a"/>–<paramref name="b"/>, mm.</summary>
        public static double DistanceToSegment(Pt p, Pt a, Pt b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return p.DistanceTo(a);
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            return p.DistanceTo(new Pt(a.X + t * dx, a.Y + t * dy));
        }
    }
}
