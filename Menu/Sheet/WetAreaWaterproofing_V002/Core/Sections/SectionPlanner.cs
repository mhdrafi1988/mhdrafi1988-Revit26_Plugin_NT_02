using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections
{
    /// <summary>What the planner needs to know about one room.</summary>
    /// <param name="Number">Room number, for messages.</param>
    /// <param name="Outer">Outer outline, mm, host coordinates.</param>
    /// <param name="Inside">A point inside the room (its location point).</param>
    /// <param name="Items">Doors, drains and fixtures of the room.</param>
    public sealed record RoomSectionInput(string Number, IReadOnlyList<Pt> Outer, Pt Inside, IReadOnlyList<FoundItem> Items);

    /// <summary>One reference section to place.</summary>
    /// <param name="Kind">Target kind.</param>
    /// <param name="Label">Name for the log and the report, e.g. "Floor drain 2".</param>
    /// <param name="Head">Head end of the cut line, mm.</param>
    /// <param name="Tail">Tail end of the cut line, mm.</param>
    /// <param name="CutElementId">Element the cut goes through; 0 for cuts through the room or a wall.</param>
    /// <param name="CutCategory">Its category, or empty.</param>
    /// <param name="CutName">Its "Family : Type", or empty.</param>
    /// <param name="CutModel">"Host", a link name, or empty.</param>
    /// <param name="DraftingViewName">Drafting view the section points to (empty for callouts).</param>
    /// <param name="Mode">Reference section or callout.</param>
    public sealed record PlannedSection(
        SectionTargetKind Kind, string Label, Pt Head, Pt Tail,
        long CutElementId, string CutCategory, string CutName, string CutModel, string DraftingViewName,
        SectionMode Mode = SectionMode.ReferenceSection);

    /// <summary>The sections for one room, and what was left out and why.</summary>
    public sealed record SectionPlan(IReadOnlyList<PlannedSection> Sections, IReadOnlyList<string> Skipped);

    /// <summary>Decides which reference sections a room gets and where each cut line runs.</summary>
    public static class SectionPlanner
    {
        /// <summary>A wall-corner cut sits this far from the corner, mm.</summary>
        public const double CornerOffsetMm = 300;

        /// <summary>
        /// Plans the sections for <paramref name="room"/>. Targets are taken in table order; a room
        /// never gets more than <paramref name="max"/>, and what does not fit, or has no match, is
        /// listed in <see cref="SectionPlan.Skipped"/>. Nothing is invented: a room without a drain
        /// gets no drain section.
        /// </summary>
        public static SectionPlan Plan(RoomSectionInput room, IReadOnlyList<SectionTargetSetting> targets,
            bool oneDraftingViewForAll, string sharedDraftingViewName, int max = SectionTargets.MaxPerRoom)
        {
            var sections = new List<PlannedSection>();
            var skipped = new List<string>();
            if (room?.Outer == null || room.Outer.Count < 3 || targets == null) return new SectionPlan(sections, skipped);
            string prefix = $"Room {room.Number}: ";

            foreach (SectionTargetSetting target in targets.Where(t => t != null && t.On))
            {
                string label = SectionTargets.Label(target.Kind);
                List<Cut> cuts = Cuts(room, target);
                if (cuts.Count == 0)
                {
                    skipped.Add($"{prefix}no {label.ToLowerInvariant()} found; section skipped.");
                    continue;
                }
                if (!target.AllMatches) cuts = cuts.Take(1).ToList();

                string view = !SectionModes.UsesDraftingView(target.Mode) ? string.Empty
                    : oneDraftingViewForAll || string.IsNullOrWhiteSpace(target.DraftingViewName)
                        ? (string.IsNullOrWhiteSpace(sharedDraftingViewName) ? SectionTargets.DefaultDraftingName(target.Kind) : sharedDraftingViewName)
                        : target.DraftingViewName;

                for (int i = 0; i < cuts.Count; i++)
                {
                    string name = cuts.Count > 1 ? $"{label} {i + 1}" : label;
                    if (sections.Count >= max)
                    {
                        skipped.Add($"{prefix}limit of {max} sections reached; {name} skipped.");
                        continue;
                    }
                    Cut c = cuts[i];
                    sections.Add(new PlannedSection(target.Kind, name, c.Head, c.Tail,
                        c.Item?.ElementId ?? 0, c.Item?.Category ?? string.Empty, c.Item?.Name ?? string.Empty,
                        c.Item?.Model ?? string.Empty, view, target.Mode));
                }
            }
            return new SectionPlan(sections, skipped);
        }

        private sealed record Cut(Pt Head, Pt Tail, FoundItem Item);

        /// <summary>Every cut the target could make in the room, nearest the room's inside point first.</summary>
        private static List<Cut> Cuts(RoomSectionInput room, SectionTargetSetting target)
        {
            IReadOnlyList<Pt> o = room.Outer;
            double length = target.LengthMm > 0 ? target.LengthMm : SectionTargets.DefaultLengthMm(target.Kind);
            var cuts = new List<Cut>();

            switch (target.Kind)
            {
                case SectionTargetKind.FullRoomH:
                    cuts.Add(new Cut(new Pt(o.Min(p => p.X) - length, room.Inside.Y), new Pt(o.Max(p => p.X) + length, room.Inside.Y), null));
                    break;

                case SectionTargetKind.FullRoomV:
                    cuts.Add(new Cut(new Pt(room.Inside.X, o.Min(p => p.Y) - length), new Pt(room.Inside.X, o.Max(p => p.Y) + length), null));
                    break;

                case SectionTargetKind.WallFloor:
                {
                    int longest = 0;
                    for (int i = 1; i < o.Count; i++)
                        if (EdgeLength(o, i) > EdgeLength(o, longest) + 1e-6) longest = i;
                    cuts.Add(Across(o, longest, o[longest].MidTo(o[(longest + 1) % o.Count]), length, null));
                    break;
                }

                case SectionTargetKind.WallCorner:
                    foreach (int i in Enumerable.Range(0, o.Count).OrderBy(i => o[i].DistanceTo(room.Inside)))
                    {
                        int previous = (i - 1 + o.Count) % o.Count;
                        bool nextIsLonger = EdgeLength(o, i) >= EdgeLength(o, previous);
                        int edge = nextIsLonger ? i : previous;
                        Pt other = nextIsLonger ? o[(i + 1) % o.Count] : o[previous];
                        double along = Math.Min(CornerOffsetMm, o[i].DistanceTo(other) / 2);
                        double d = o[i].DistanceTo(other);
                        if (d < 1e-6) continue;
                        var at = new Pt(o[i].X + (other.X - o[i].X) / d * along, o[i].Y + (other.Y - o[i].Y) / d * along);
                        cuts.Add(Across(o, edge, at, length, null));
                    }
                    break;

                default: // Door, Drain, Wc, Shower: through the item, square to the nearest wall
                    foreach (FoundItem item in (room.Items ?? new List<FoundItem>())
                        .Where(i => i != null && i.Kind == target.Kind).OrderBy(i => i.At.DistanceTo(room.Inside)))
                        cuts.Add(Across(o, Polyline2D.Nearest(o, item.At).EdgeIndex, item.At, length, item));
                    break;
            }
            return cuts;
        }

        private static double EdgeLength(IReadOnlyList<Pt> o, int i) => o[i].DistanceTo(o[(i + 1) % o.Count]);

        /// <summary>A cut of <paramref name="length"/> centred on <paramref name="at"/>, square to edge <paramref name="edge"/>.</summary>
        private static Cut Across(IReadOnlyList<Pt> o, int edge, Pt at, double length, FoundItem item)
        {
            Pt a = o[edge], b = o[(edge + 1) % o.Count];
            double d = a.DistanceTo(b);
            double nx = d < 1e-9 ? 0 : -(b.Y - a.Y) / d, ny = d < 1e-9 ? 1 : (b.X - a.X) / d; // left of the edge = inside (outline is counter-clockwise)
            double half = length / 2;
            return new Cut(new Pt(at.X - nx * half, at.Y - ny * half), new Pt(at.X + nx * half, at.Y + ny * half), item);
        }
    }

    /// <summary>Builds and cleans view names.</summary>
    public static class ViewNames
    {
        private const string Illegal = "\\:{}[]|;<>?`~";

        /// <summary>Replaces {Level}, {Number} and {Name} in <paramref name="pattern"/> and cleans the result.</summary>
        public static string Fill(string pattern, string level, string number, string name)
        {
            string text = (pattern ?? string.Empty)
                .Replace("{Level}", Clean(level)).Replace("{Number}", Clean(number)).Replace("{Name}", Clean(name));
            text = Clean(text);
            return text.Length == 0 ? "WP" : text;
        }

        /// <summary>Replaces characters Revit does not allow in a view name with '-', and tidies spaces.</summary>
        public static string Clean(string text)
        {
            var chars = (text ?? string.Empty).Select(c => Illegal.IndexOf(c) >= 0 || char.IsControl(c) ? '-' : c).ToArray();
            return string.Join(" ", new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary><paramref name="name"/>, or "name (2)", "name (3)"… when it is in <paramref name="taken"/>.</summary>
        public static string Unique(string name, ICollection<string> taken)
        {
            if (taken == null || !taken.Contains(name)) return name;
            for (int n = 2; ; n++)
            {
                string candidate = $"{name} ({n})";
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
