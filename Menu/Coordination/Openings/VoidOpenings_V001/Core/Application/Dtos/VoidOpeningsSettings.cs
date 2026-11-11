using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos
{
    /// <summary>One clearance rule as the user edits and saves it. Enum values are kept as names so the file stays readable.</summary>
    public sealed class ClearanceRuleDto
    {
        /// <summary>MEP category name (Pipe, Duct, CableTray, Conduit, or a fitting / accessory category).</summary>
        public string Category { get; set; } = nameof(MepCategory.Pipe);

        /// <summary>Smallest size covered, millimetres.</summary>
        public double FromMm { get; set; }

        /// <summary>Largest size covered, millimetres.</summary>
        public double ToMm { get; set; }

        /// <summary>Offset, millimetres.</summary>
        public double ClearanceMm { get; set; }

        /// <summary>PerSide or Total.</summary>
        public string Mode { get; set; } = nameof(ClearanceMode.PerSide);

        /// <summary>Auto, Circular or Rectangular.</summary>
        public string Shape { get; set; } = nameof(OpeningShapePolicy.Auto);
    }

    /// <summary>
    /// Everything the user configures, saved as <c>settings.json</c> under
    /// <c>%AppData%\Revit26_Plugin_Revised\VoidOpenings</c>. A missing field takes its default, so files
    /// written by older versions keep loading.
    /// </summary>
    public sealed class VoidOpeningsSettings
    {
        /// <summary>Version written by this build.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Format version of the file.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Clearance rules in the user's order.</summary>
        public List<ClearanceRuleDto> Rules { get; set; } = new List<ClearanceRuleDto>();

        /// <summary>Offset used when no rule fits, millimetres.</summary>
        public double DefaultClearanceMm { get; set; } = 50;

        /// <summary>How the default offset is applied: PerSide or Total.</summary>
        public string DefaultMode { get; set; } = nameof(ClearanceMode.PerSide);

        /// <summary>How far the void passes beyond the structure on each side, millimetres.</summary>
        public double OvershootMm { get; set; } = 50;

        /// <summary>Sizes are rounded up to a multiple of this; 0 turns rounding off.</summary>
        public double RoundUpToMm { get; set; } = 10;

        /// <summary>True to add insulation thickness to the MEP size.</summary>
        public bool IncludeInsulation { get; set; } = true;

        /// <summary>PerpendicularToStructure or FollowMep.</summary>
        public string AxisMode { get; set; } = nameof(OpeningAxisMode.PerpendicularToStructure);

        /// <summary>Largest crossing angle at which a round element keeps a circular opening, degrees.</summary>
        public double MaxCircularAngleDeg { get; set; } = 15;

        /// <summary>Name of a loaded family to use for circular voids instead of the built-in one; empty for built-in.</summary>
        public string CustomRoundFamily { get; set; } = string.Empty;

        /// <summary>Name of a loaded family to use for rectangular voids instead of the built-in one; empty for built-in.</summary>
        public string CustomRectFamily { get; set; } = string.Empty;

        /// <summary>The example rules of the brief, each side.</summary>
        public static VoidOpeningsSettings CreateDefault()
        {
            var settings = new VoidOpeningsSettings();
            void Add(MepCategory category, double from, double to, double clearance)
                => settings.Rules.Add(new ClearanceRuleDto { Category = category.ToString(), FromMm = from, ToMm = to, ClearanceMm = clearance });
            Add(MepCategory.Conduit, 0, 25, 25);
            Add(MepCategory.Conduit, 26, 50, 40);
            Add(MepCategory.Conduit, 51, 100, 50);
            Add(MepCategory.Pipe, 0, 50, 25);
            Add(MepCategory.Pipe, 51, 100, 40);
            Add(MepCategory.Pipe, 101, 200, 50);
            Add(MepCategory.CableTray, 0, 300, 50);
            Add(MepCategory.Duct, 0, 500, 75);
            return settings;
        }

        /// <summary>The rules as the domain rule set. Unknown names fall back to the default of their kind.</summary>
        public ClearanceRuleSet ToRuleSet() => new ClearanceRuleSet(
            Rules.Select(r => new ClearanceRule(
                Parse(r.Category, MepCategory.Pipe), r.FromMm, r.ToMm, Math.Max(0, r.ClearanceMm),
                Parse(r.Mode, ClearanceMode.PerSide), Parse(r.Shape, OpeningShapePolicy.Auto))),
            Math.Max(0, DefaultClearanceMm), Parse(DefaultMode, ClearanceMode.PerSide));

        /// <summary>The run options as the domain settings.</summary>
        public OpeningSettings ToOpeningSettings() => new OpeningSettings
        {
            OvershootMm = Math.Max(0, OvershootMm),
            RoundUpToMm = Math.Max(0, RoundUpToMm),
            IncludeInsulation = IncludeInsulation,
            AxisMode = Parse(AxisMode, OpeningAxisMode.PerpendicularToStructure),
            MaxCircularAngleDeg = Math.Min(89, Math.Max(0, MaxCircularAngleDeg)),
        };

        /// <summary>Parses an enum name, returning <paramref name="fallback"/> for anything unknown.</summary>
        public static TEnum Parse<TEnum>(string name, TEnum fallback) where TEnum : struct
            => Enum.TryParse(name, true, out TEnum value) && Enum.IsDefined(typeof(TEnum), value) ? value : fallback;
    }
}
