using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Matching;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Sections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Mapping
{
    /// <summary>Where a mapping row's cut goes. Values are stored in the settings file: append only.</summary>
    public enum MappingCut
    {
        /// <summary>Through each matching element, square to the nearest wall (a door: its threshold).</summary>
        ThroughElement = 0,
        /// <summary>The middle of the room's longest wall.</summary>
        LongestWall = 1,
        /// <summary>The corners of the room (the upturn at the wall).</summary>
        RoomCorners = 2,
        /// <summary>Through the whole room, left to right.</summary>
        FullRoomH = 3,
        /// <summary>Through the whole room, bottom to top.</summary>
        FullRoomV = 4,
        /// <summary>Through the whole room, both ways.</summary>
        FullRoomHV = 5
    }

    /// <summary>How a mapping row picks the types of its category. Values are stored: append only.</summary>
    public enum TypeMatch
    {
        /// <summary>Every type of the category.</summary>
        Any = 0,
        /// <summary>Types whose "Family : Type" name contains a keyword (word start).</summary>
        Keywords = 1,
        /// <summary>Only the picked "Family : Type" names.</summary>
        Types = 2
    }

    /// <summary>One row of the Mapping table: category → type → cut → what is placed (tool 3.0.0).</summary>
    public sealed class MappingRule
    {
        /// <summary>Stable id of the row, so its matches and Review keys survive a reorder.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        /// <summary>Cuts are made for this row.</summary>
        public bool On { get; set; } = true;
        /// <summary>Name shown in the Review and report, e.g. "Floor drain".</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Revit category name (e.g. "Doors"), or <see cref="MappingRules.RoomCategory"/>.</summary>
        public string Category { get; set; } = string.Empty;
        /// <summary>How the types are picked.</summary>
        public TypeMatch Match { get; set; } = TypeMatch.Any;
        /// <summary>Keywords for <see cref="TypeMatch.Keywords"/> (lower case).</summary>
        public List<string> Keywords { get; set; } = new();
        /// <summary>"Family : Type" names for <see cref="TypeMatch.Types"/>.</summary>
        public List<string> TypeNames { get; set; } = new();
        /// <summary>Where the cut goes.</summary>
        public MappingCut Cut { get; set; } = MappingCut.ThroughElement;
        /// <summary>What is placed on the room plan.</summary>
        public SectionMode Mode { get; set; } = SectionMode.ReferenceSection;
        /// <summary>One cut per match; off = only the match nearest the room centre.</summary>
        public bool AllMatches { get; set; }
        /// <summary>Length of the cut line, mm. For full-room cuts: how far it runs past the room on each side.</summary>
        public double LengthMm { get; set; } = 1200;
        /// <summary>Drafting view a Ref. Section / Ref. Callout points to.</summary>
        public string DraftingViewName { get; set; } = string.Empty;
    }

    /// <summary>A category the "+ Add category" search can offer.</summary>
    /// <param name="Name">Category name.</param>
    /// <param name="ModelCount">In how many of the ticked models it has elements.</param>
    public sealed record CategoryChoice(string Name, int ModelCount);

    /// <summary>A type found by "Load types".</summary>
    /// <param name="Category">Category name.</param>
    /// <param name="TypeName">"Family : Type".</param>
    /// <param name="Model">"Host" or the link's name.</param>
    /// <param name="Count">Placed elements of the type in that model.</param>
    public sealed record LoadedType(string Category, string TypeName, string Model, int Count);

    /// <summary>Defaults, migration, matching and conversion of the Mapping table.</summary>
    public static class MappingRules
    {
        /// <summary>Pseudo category of the full-room rows: they need no element.</summary>
        public const string RoomCategory = "Room";
        /// <summary>Doors category name.</summary>
        public const string Doors = "Doors";
        /// <summary>Walls category name.</summary>
        public const string Walls = "Walls";
        /// <summary>Plumbing Fixtures category name.</summary>
        public const string PlumbingFixtures = "Plumbing Fixtures";
        /// <summary>Pipe Accessories category name.</summary>
        public const string PipeAccessories = "Pipe Accessories";

        /// <summary>Categories loaded when nothing is saved yet.</summary>
        public static IReadOnlyList<string> DefaultCategories { get; } = new[] { Doors, Walls, PlumbingFixtures, PipeAccessories };

        /// <summary>Text shown for a cut.</summary>
        public static string CutLabel(MappingCut cut, string category = null) => cut switch
        {
            MappingCut.ThroughElement => category == Doors ? "Threshold" : "Through element",
            MappingCut.LongestWall => "Longest wall, middle",
            MappingCut.RoomCorners => "Room corners",
            MappingCut.FullRoomH => "Full room, horizontal",
            MappingCut.FullRoomV => "Full room, vertical",
            _ => "Full room, H + V"
        };

        /// <summary>The cuts a row of <paramref name="category"/> can use, in list order.</summary>
        public static IReadOnlyList<MappingCut> CutsFor(string category)
        {
            if (category == RoomCategory) return new[] { MappingCut.FullRoomHV, MappingCut.FullRoomH, MappingCut.FullRoomV };
            if (category == Walls) return new[] { MappingCut.LongestWall, MappingCut.RoomCorners, MappingCut.ThroughElement };
            return new[] { MappingCut.ThroughElement };
        }

        /// <summary>Default cut length, mm, for <paramref name="cut"/>.</summary>
        public static double DefaultLengthMm(MappingCut cut, string category) => cut switch
        {
            MappingCut.FullRoomH or MappingCut.FullRoomV or MappingCut.FullRoomHV => 300,
            MappingCut.LongestWall or MappingCut.RoomCorners => 1000,
            _ => category == Doors ? 1500 : 1200
        };

        private static MappingRule Rule(string id, bool on, string name, string category, TypeMatch match, string[] keywords,
            MappingCut cut, SectionMode mode, string view, bool allMatches = false) => new()
        {
            Id = id, On = on, Name = name, Category = category, Match = match,
            Keywords = (keywords ?? Array.Empty<string>()).ToList(), Cut = cut, Mode = mode, AllMatches = allMatches,
            LengthMm = DefaultLengthMm(cut, category), DraftingViewName = view ?? string.Empty
        };

        /// <summary>The default table (approved for 3.0.0): doors, walls, WC, shower, basin, drain and the full-room cut.</summary>
        public static List<MappingRule> Defaults() => new()
        {
            Rule("door", true, "Door threshold", Doors, TypeMatch.Any, null, MappingCut.ThroughElement, SectionMode.ReferenceSection, "WP Detail - Door Threshold"),
            Rule("wallup", true, "Wall upturn", Walls, TypeMatch.Any, null, MappingCut.LongestWall, SectionMode.ReferenceSection, "WP Detail - Wall Upturn"),
            Rule("corner", false, "Wall corner", Walls, TypeMatch.Any, null, MappingCut.RoomCorners, SectionMode.ReferenceCallout, "WP Detail - Wall Corner"),
            Rule("wc", true, "WC", PlumbingFixtures, TypeMatch.Keywords, new[] { "wc", "toilet", "water closet", "closet" }, MappingCut.ThroughElement, SectionMode.ReferenceSection, "WP Detail - WC Base"),
            Rule("shower", true, "Shower / bath", PlumbingFixtures, TypeMatch.Keywords, new[] { "shower", "bath", "tub" }, MappingCut.ThroughElement, SectionMode.ReferenceSection, "WP Detail - Shower"),
            Rule("basin", false, "Basin", PlumbingFixtures, TypeMatch.Keywords, new[] { "basin", "sink", "lavatory" }, MappingCut.ThroughElement, SectionMode.Callout, string.Empty),
            Rule("drain", true, "Floor drain", PipeAccessories, TypeMatch.Keywords, new[] { "drain", "fd", "gully", "floor waste" }, MappingCut.ThroughElement, SectionMode.ReferenceCallout, "WP Detail - Floor Drain"),
            Rule("room", true, "Full room", RoomCategory, TypeMatch.Any, null, MappingCut.FullRoomHV, SectionMode.Section, string.Empty)
        };

        /// <summary>
        /// Builds the table from a V001 / V002 settings file: each old target becomes the matching row with its
        /// on/off, mode, length, per-item choice and drafting view (the shared view when "one view for all" was on).
        /// The basin row, new in 3.0.0, starts off.
        /// </summary>
        public static List<MappingRule> FromV002(IReadOnlyList<SectionTargetSetting> targets, bool oneViewForAll, string sharedView,
            IReadOnlyList<string> drainKeywords, IReadOnlyList<string> wcKeywords, IReadOnlyList<string> showerKeywords)
        {
            List<MappingRule> rules = Defaults();
            if (targets == null || targets.Count == 0) return rules;

            SectionTargetSetting Old(SectionTargetKind k) => targets.FirstOrDefault(t => t != null && t.Kind == k);
            void Copy(string id, SectionTargetSetting old)
            {
                MappingRule r = rules.First(x => x.Id == id);
                if (old == null) { r.On = false; return; }
                r.On = old.On;
                r.Mode = old.Mode;
                r.AllMatches = old.AllMatches;
                if (old.LengthMm > 0) r.LengthMm = old.LengthMm;
                string view = oneViewForAll && !string.IsNullOrWhiteSpace(sharedView) ? sharedView : old.DraftingViewName;
                if (!string.IsNullOrWhiteSpace(view)) r.DraftingViewName = view;
            }

            Copy("door", Old(SectionTargetKind.Door));
            Copy("wallup", Old(SectionTargetKind.WallFloor));
            Copy("corner", Old(SectionTargetKind.WallCorner));
            Copy("wc", Old(SectionTargetKind.Wc));
            Copy("shower", Old(SectionTargetKind.Shower));
            Copy("drain", Old(SectionTargetKind.Drain));
            rules.First(x => x.Id == "basin").On = false;

            SectionTargetSetting h = Old(SectionTargetKind.FullRoomH), v = Old(SectionTargetKind.FullRoomV);
            MappingRule room = rules.First(x => x.Id == "room");
            bool hOn = h?.On == true, vOn = v?.On == true;
            Copy("room", hOn ? h : vOn ? v : h ?? v);
            room.On = hOn || vOn;
            room.Cut = hOn && vOn ? MappingCut.FullRoomHV : vOn ? MappingCut.FullRoomV : hOn ? MappingCut.FullRoomH : MappingCut.FullRoomHV;

            void Words(string id, IReadOnlyList<string> words)
            {
                if (words != null && words.Count > 0) rules.First(x => x.Id == id).Keywords = words.ToList();
            }
            Words("wc", wcKeywords);
            Words("shower", showerKeywords);
            Words("drain", drainKeywords);
            return rules;
        }

        /// <summary>Removes nulls and duplicate ids, cleans names, keywords and lengths. Order is kept.</summary>
        public static List<MappingRule> Normalize(List<MappingRule> rules)
        {
            var result = new List<MappingRule>();
            var ids = new HashSet<string>();
            foreach (MappingRule r in rules ?? new List<MappingRule>())
            {
                if (r == null) continue;
                if (string.IsNullOrWhiteSpace(r.Id) || !ids.Add(r.Id))
                {
                    r.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
                    ids.Add(r.Id);
                }
                r.Category = (r.Category ?? string.Empty).Trim();
                if (r.Category.Length == 0) continue;
                if (!Enum.IsDefined(typeof(MappingCut), r.Cut) || !CutsFor(r.Category).Contains(r.Cut)) r.Cut = CutsFor(r.Category)[0];
                if (!Enum.IsDefined(typeof(TypeMatch), r.Match)) r.Match = TypeMatch.Any;
                if (!Enum.IsDefined(typeof(SectionMode), r.Mode)) r.Mode = SectionMode.ReferenceSection;
                r.Name = string.IsNullOrWhiteSpace(r.Name) ? r.Category : r.Name.Trim();
                r.Keywords = (r.Keywords ?? new List<string>()).Select(k => (k ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(k => k.Length > 0).Distinct().ToList();
                r.TypeNames = (r.TypeNames ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
                if (!(r.LengthMm > 0) || r.LengthMm > 20000) r.LengthMm = DefaultLengthMm(r.Cut, r.Category);
                r.DraftingViewName = (r.DraftingViewName ?? string.Empty).Trim();
                result.Add(r);
            }
            return result;
        }

        /// <summary>True when an element of <paramref name="category"/> called <paramref name="typeName"/> ("Family : Type") belongs to the row.</summary>
        public static bool Matches(MappingRule rule, string category, string typeName)
        {
            if (rule == null || rule.Category == RoomCategory || !string.Equals(rule.Category, category, StringComparison.OrdinalIgnoreCase)) return false;
            return rule.Match switch
            {
                TypeMatch.Keywords => rule.Keywords.Count > 0 && KeywordMatcher.Match(typeName ?? string.Empty, rule.Keywords) != null,
                TypeMatch.Types => rule.TypeNames.Contains(typeName ?? string.Empty),
                _ => true
            };
        }

        /// <summary>Placed elements that the row matches among <paramref name="types"/>.</summary>
        public static int CountFound(MappingRule rule, IEnumerable<LoadedType> types)
            => (types ?? Enumerable.Empty<LoadedType>()).Where(t => Matches(rule, t.Category, t.TypeName)).Sum(t => t.Count);

        /// <summary>
        /// The section targets the planner works on, in table order: one per row, two for a "Full room, H + V" row.
        /// Rows of a category that is not loaded are left out (their elements are never read).
        /// </summary>
        public static List<SectionTargetSetting> ToTargets(IReadOnlyList<MappingRule> rules, ICollection<string> loadedCategories = null)
        {
            var targets = new List<SectionTargetSetting>();
            foreach (MappingRule r in rules ?? Array.Empty<MappingRule>())
            {
                if (r == null) continue;
                bool needsElements = r.Category != RoomCategory && (r.Cut == MappingCut.ThroughElement || r.Match != TypeMatch.Any);
                bool loaded = loadedCategories == null || r.Category == RoomCategory
                    || loadedCategories.Any(c => string.Equals(c, r.Category, StringComparison.OrdinalIgnoreCase));
                SectionTargetSetting T(SectionTargetKind kind, string label) => new()
                {
                    Kind = kind, On = r.On && (loaded || !needsElements), AllMatches = r.AllMatches, LengthMm = r.LengthMm,
                    DraftingViewName = r.DraftingViewName, Mode = r.Mode, RuleId = r.Id, Label = label,
                    WallTypeFilter = r.Category != RoomCategory && (r.Cut is MappingCut.LongestWall or MappingCut.RoomCorners) && r.Match != TypeMatch.Any
                };
                switch (r.Cut)
                {
                    case MappingCut.FullRoomHV:
                        targets.Add(T(SectionTargetKind.FullRoomH, r.Name + " – H"));
                        targets.Add(T(SectionTargetKind.FullRoomV, r.Name + " – V"));
                        break;
                    case MappingCut.FullRoomH: targets.Add(T(SectionTargetKind.FullRoomH, r.Name)); break;
                    case MappingCut.FullRoomV: targets.Add(T(SectionTargetKind.FullRoomV, r.Name)); break;
                    case MappingCut.LongestWall: targets.Add(T(SectionTargetKind.WallFloor, r.Name)); break;
                    case MappingCut.RoomCorners: targets.Add(T(SectionTargetKind.WallCorner, r.Name)); break;
                    default: targets.Add(T(r.Category == Doors ? SectionTargetKind.Door : SectionTargetKind.Element, r.Name)); break;
                }
            }
            return targets;
        }
    }
}
