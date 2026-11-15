using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Sections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models
{
    /// <summary>
    /// Persisted to %AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\settings.json.
    /// Lengths are millimetres.
    /// </summary>
    public sealed class WetAreaSettings
    {
        /// <summary>Current settings-file format version.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Default snap distance for small outline gaps, mm.</summary>
        public const double DefaultSnapMm = 5;

        /// <summary>Default largest gap closed with a straight bridge, mm.</summary>
        public const double DefaultBridgeMm = 50;

        /// <summary>Default allowed difference between outline area and room area, percent.</summary>
        public const double DefaultAreaTolerancePct = 2;

        /// <summary>Room-name keywords that mark a wet area.</summary>
        public static IReadOnlyList<string> DefaultKeywords { get; } =
            new[] { "toilet", "wc", "bath", "shower", "kitchen", "pantry", "laundry", "wash", "balcony" };

        /// <summary>Folder the Excel reports go to unless the user picks another.</summary>
        public static string DefaultReportFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "WetAreaWaterproofing", "Reports");

        /// <summary>Format version of the file this was read from.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Room-name keywords that mark a wet area (lower case).</summary>
        public List<string> Keywords { get; set; } = DefaultKeywords.ToList();

        /// <summary>Outline gaps up to this size are closed by moving the ends together, mm.</summary>
        public double SnapMm { get; set; } = DefaultSnapMm;

        /// <summary>Outline gaps up to this size are closed with a straight line, mm.</summary>
        public double BridgeMm { get; set; } = DefaultBridgeMm;

        /// <summary>Allowed difference between the outline area and the room area, percent.</summary>
        public double AreaTolerancePct { get; set; } = DefaultAreaTolerancePct;

        /// <summary>Use an approximate outline (hull) when every repair fails. Off by default.</summary>
        public bool AllowApproximate { get; set; }

        /// <summary>Roof type for the waterproofing, by name; empty = first in the project.</summary>
        public string RoofTypeName { get; set; } = string.Empty;

        /// <summary>Roof height above the slab top (or above the level when there is no slab), mm.</summary>
        public double OffsetMm { get; set; }

        /// <summary>What to do when no slab is found under a room.</summary>
        public NoSlabMode NoSlab { get; set; } = NoSlabMode.LevelPlusOffset;

        /// <summary>Workset for new elements, by name; empty = the active workset.</summary>
        public string WorksetName { get; set; } = string.Empty;

        /// <summary>Read rooms from the host model.</summary>
        public bool IncludeHost { get; set; } = true;

        /// <summary>Link instance names to read rooms from; empty = every loaded link.</summary>
        public List<string> IncludedLinks { get; set; } = new();

        /// <summary>Read rooms from links at all.</summary>
        public bool IncludeLinks { get; set; } = true;

        /// <summary>Save the Excel report after every run.</summary>
        public bool AutoSaveReport { get; set; } = true;

        /// <summary>Folder for the Excel reports; empty = <see cref="DefaultReportFolder"/>.</summary>
        public string ReportFolder { get; set; } = string.Empty;

        /// <summary>Add the room number and name to the roof's Comments. The run ID and room key are always written.</summary>
        public bool WriteComments { get; set; } = true;

        /// <summary>What a run does with rooms that already have a roof from an earlier run.</summary>
        public ReRunMode ReRun { get; set; } = ReRunMode.UpdateInPlace;

        // ── Part B: plans and reference sections (added in tool 1.1.0; missing in older files = these defaults) ──

        /// <summary>Default name of the drafting view shared by all targets.</summary>
        public const string DefaultSharedDraftingViewName = "WP Detail - Typical Wet Area";

        /// <summary>Default plan name pattern.</summary>
        public const string DefaultPlanNamePattern = "WP - {Level} - {Number} {Name}";

        /// <summary>Run step: create the waterproofing roofs.</summary>
        public bool CreateRoofs { get; set; } = true;

        /// <summary>Run step: create a cropped plan per room.</summary>
        public bool CreatePlans { get; set; } = true;

        /// <summary>Run step: place the reference sections on the room's plan.</summary>
        public bool CreateSections { get; set; } = true;

        /// <summary>Floor plan type for the room plans, by name; empty = first in the project.</summary>
        public string PlanViewTypeName { get; set; } = string.Empty;

        /// <summary>View template for the room plans, by name; empty = none.</summary>
        public string PlanTemplateName { get; set; } = string.Empty;

        /// <summary>The plan's crop is the room's outline box plus this margin, mm.</summary>
        public double CropMarginMm { get; set; } = 500;

        /// <summary>Plan scale, 1:n.</summary>
        public int PlanScale { get; set; } = 100;

        /// <summary>Plan name; {Level}, {Number} and {Name} are filled in.</summary>
        public string PlanNamePattern { get; set; } = DefaultPlanNamePattern;

        /// <summary>The "Section targets" table, in order; rows higher up win when a room would get more than six.</summary>
        public List<SectionTargetSetting> Targets { get; set; } = SectionTargets.Defaults();

        /// <summary>All targets point to one drafting view; off = each target has its own.</summary>
        public bool OneDraftingViewForAll { get; set; } = true;

        /// <summary>Name of the drafting view shared by all targets.</summary>
        public string SharedDraftingViewName { get; set; } = DefaultSharedDraftingViewName;

        /// <summary>Scale of drafting views the tool creates, 1:n.</summary>
        public int DraftingScale { get; set; } = 100;

        /// <summary>Fixture names that mark a floor drain.</summary>
        public List<string> DrainKeywords { get; set; } = new() { "drain", "fd", "floor drain" };

        /// <summary>Fixture names that mark a WC.</summary>
        public List<string> WcKeywords { get; set; } = new() { "wc", "closet", "toilet" };

        /// <summary>Fixture names that mark a shower or bath.</summary>
        public List<string> ShowerKeywords { get; set; } = new() { "shower", "bath", "tray" };

        /// <summary>Unticked rows of the "Search targets in" list, as "Model|Category".</summary>
        public List<string> SourcesOff { get; set; } = new();

        /// <summary>Room grid filters, sort, grouping and hidden columns.</summary>
        public GridState Grid { get; set; } = new();

        private static List<string> CleanKeywords(List<string> keywords, string[] fallback)
        {
            var cleaned = (keywords ?? new List<string>())
                .Select(k => (k ?? string.Empty).Trim().ToLowerInvariant()).Where(k => k.Length > 0).Distinct().ToList();
            return cleaned.Count > 0 ? cleaned : fallback.ToList();
        }

        /// <summary>Fills missing values and clamps the numbers to usable ranges. Returns this.</summary>
        public WetAreaSettings Normalize()
        {
            Version = CurrentVersion;
            Keywords = (Keywords ?? new List<string>())
                .Select(k => (k ?? string.Empty).Trim().ToLowerInvariant())
                .Where(k => k.Length > 0)
                .Distinct()
                .ToList();
            if (Keywords.Count == 0) Keywords = DefaultKeywords.ToList();

            if (!(SnapMm > 0) || SnapMm > 100) SnapMm = DefaultSnapMm;
            if (!(BridgeMm > 0) || BridgeMm > 1000) BridgeMm = DefaultBridgeMm;
            if (BridgeMm < SnapMm) BridgeMm = SnapMm;
            if (!(AreaTolerancePct > 0) || AreaTolerancePct > 50) AreaTolerancePct = DefaultAreaTolerancePct;
            if (double.IsNaN(OffsetMm) || double.IsInfinity(OffsetMm)) OffsetMm = 0;

            Targets = SectionTargets.Normalize(Targets);
            if (!(CropMarginMm >= 0) || CropMarginMm > 10000) CropMarginMm = 500;
            if (PlanScale < 1 || PlanScale > 5000) PlanScale = 100;
            if (DraftingScale < 1 || DraftingScale > 5000) DraftingScale = 100;
            if (string.IsNullOrWhiteSpace(PlanNamePattern)) PlanNamePattern = DefaultPlanNamePattern;
            if (string.IsNullOrWhiteSpace(SharedDraftingViewName)) SharedDraftingViewName = DefaultSharedDraftingViewName;
            PlanViewTypeName ??= string.Empty;
            PlanTemplateName ??= string.Empty;
            DrainKeywords = CleanKeywords(DrainKeywords, new[] { "drain", "fd", "floor drain" });
            WcKeywords = CleanKeywords(WcKeywords, new[] { "wc", "closet", "toilet" });
            ShowerKeywords = CleanKeywords(ShowerKeywords, new[] { "shower", "bath", "tray" });
            SourcesOff ??= new List<string>();
            Grid = (Grid ?? new GridState()).Normalize();
            RoofTypeName ??= string.Empty;
            WorksetName ??= string.Empty;
            IncludedLinks ??= new List<string>();
            if (string.IsNullOrWhiteSpace(ReportFolder)) ReportFolder = DefaultReportFolder;
            return this;
        }
    }
}
