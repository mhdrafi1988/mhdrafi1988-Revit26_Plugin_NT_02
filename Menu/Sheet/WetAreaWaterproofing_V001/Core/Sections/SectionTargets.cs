using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Sections
{
    /// <summary>What a reference section is cut at. Values are stored in the settings file: append only.</summary>
    public enum SectionTargetKind
    {
        /// <summary>Through the whole room, left to right.</summary>
        FullRoomH = 0,
        /// <summary>Through the whole room, bottom to top.</summary>
        FullRoomV = 1,
        /// <summary>A door of the room: the threshold.</summary>
        Door = 2,
        /// <summary>A floor drain.</summary>
        Drain = 3,
        /// <summary>A corner of the room: the upturn at the wall.</summary>
        WallCorner = 4,
        /// <summary>A WC.</summary>
        Wc = 5,
        /// <summary>A shower or bath.</summary>
        Shower = 6,
        /// <summary>The middle of the longest wall.</summary>
        WallFloor = 7
    }

    /// <summary>One row of the "Section targets" table, as saved in the settings.</summary>
    public sealed class SectionTargetSetting
    {
        /// <summary>What is cut.</summary>
        public SectionTargetKind Kind { get; set; }
        /// <summary>Sections are made for this target.</summary>
        public bool On { get; set; }
        /// <summary>One section per match; off = only the match nearest the room centre.</summary>
        public bool AllMatches { get; set; }
        /// <summary>Length of the cut line, mm. For the full-room cuts: how far the line runs past the room on each side.</summary>
        public double LengthMm { get; set; }
        /// <summary>Drafting view this target points to when each target has its own; empty = the default name.</summary>
        public string DraftingViewName { get; set; } = string.Empty;
    }

    /// <summary>Names and defaults of the section targets.</summary>
    public static class SectionTargets
    {
        /// <summary>Most sections a room may get.</summary>
        public const int MaxPerRoom = 6;

        /// <summary>Every kind, in the default order of the table.</summary>
        public static IReadOnlyList<SectionTargetKind> AllKinds { get; } = new[]
        {
            SectionTargetKind.FullRoomH, SectionTargetKind.FullRoomV, SectionTargetKind.Door, SectionTargetKind.Drain,
            SectionTargetKind.WallCorner, SectionTargetKind.Wc, SectionTargetKind.Shower, SectionTargetKind.WallFloor
        };

        /// <summary>Text shown for a kind.</summary>
        public static string Label(SectionTargetKind kind) => kind switch
        {
            SectionTargetKind.FullRoomH => "Full room – horizontal",
            SectionTargetKind.FullRoomV => "Full room – vertical",
            SectionTargetKind.Door => "Door / threshold",
            SectionTargetKind.Drain => "Floor drain",
            SectionTargetKind.WallCorner => "Wall corner",
            SectionTargetKind.Wc => "WC / closet",
            SectionTargetKind.Shower => "Shower / bath",
            _ => "Wall–floor"
        };

        /// <summary>Default cut length, mm (for full-room cuts: the overshoot on each side).</summary>
        public static double DefaultLengthMm(SectionTargetKind kind) => kind switch
        {
            SectionTargetKind.FullRoomH or SectionTargetKind.FullRoomV => 300,
            SectionTargetKind.Door => 1500,
            SectionTargetKind.WallCorner or SectionTargetKind.WallFloor => 1000,
            _ => 1200
        };

        /// <summary>Default drafting view name when each target has its own view.</summary>
        public static string DefaultDraftingName(SectionTargetKind kind) => kind switch
        {
            SectionTargetKind.FullRoomH or SectionTargetKind.FullRoomV => "WP Detail - Typical Wet Area",
            SectionTargetKind.Door => "WP Detail - Door Threshold",
            SectionTargetKind.Drain => "WP Detail - Floor Drain",
            SectionTargetKind.WallCorner => "WP Detail - Wall Corner",
            SectionTargetKind.Wc => "WP Detail - WC Base",
            SectionTargetKind.Shower => "WP Detail - Shower",
            _ => "WP Detail - Wall Upturn"
        };

        /// <summary>True for targets that need a door, drain or fixture found in the model.</summary>
        public static bool NeedsItem(SectionTargetKind kind)
            => kind is SectionTargetKind.Door or SectionTargetKind.Drain or SectionTargetKind.Wc or SectionTargetKind.Shower;

        /// <summary>The default table: the two full-room cuts, door and drain on; the rest off.</summary>
        public static List<SectionTargetSetting> Defaults() => AllKinds.Select(kind => new SectionTargetSetting
        {
            Kind = kind,
            On = kind is SectionTargetKind.FullRoomH or SectionTargetKind.FullRoomV or SectionTargetKind.Door or SectionTargetKind.Drain,
            LengthMm = DefaultLengthMm(kind),
            DraftingViewName = DefaultDraftingName(kind)
        }).ToList();

        /// <summary>Removes nulls and duplicates, repairs lengths and names, and appends any kind that is missing. Order is kept.</summary>
        public static List<SectionTargetSetting> Normalize(List<SectionTargetSetting> targets)
        {
            var result = new List<SectionTargetSetting>();
            foreach (SectionTargetSetting t in targets ?? new List<SectionTargetSetting>())
            {
                if (t == null || !AllKinds.Contains(t.Kind) || result.Any(r => r.Kind == t.Kind)) continue;
                if (!(t.LengthMm > 0) || t.LengthMm > 20000) t.LengthMm = DefaultLengthMm(t.Kind);
                if (string.IsNullOrWhiteSpace(t.DraftingViewName)) t.DraftingViewName = DefaultDraftingName(t.Kind);
                result.Add(t);
            }
            foreach (SectionTargetSetting d in Defaults())
                if (result.All(r => r.Kind != d.Kind))
                {
                    if (targets != null && targets.Count > 0) d.On = false; // a kind added later never switches itself on
                    result.Add(d);
                }
            return result;
        }
    }

    /// <summary>A door, drain or fixture found in the host model or a link, in host coordinates.</summary>
    /// <param name="Kind">Door, Drain, Wc or Shower.</param>
    /// <param name="At">Plan position, mm.</param>
    /// <param name="ZMm">Height, mm.</param>
    /// <param name="ElementId">ElementId in its own model.</param>
    /// <param name="Model">"Host" or the link's name.</param>
    /// <param name="Category">Doors, Plumbing Fixtures or Pipe Accessories.</param>
    /// <param name="Name">"Family : Type".</param>
    public sealed record FoundItem(SectionTargetKind Kind, Pt At, double ZMm, long ElementId, string Model, string Category, string Name);
}
