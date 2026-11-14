using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
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

        /// <summary>Write the run ID, room number and name to the roof's Comments.</summary>
        public bool WriteComments { get; set; } = true;

        /// <summary>What a run does with rooms that already have a roof from an earlier run.</summary>
        public ReRunMode ReRun { get; set; } = ReRunMode.UpdateInPlace;

        /// <summary>Room grid filters, sort, grouping and hidden columns.</summary>
        public GridState Grid { get; set; } = new();

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

            Grid = (Grid ?? new GridState()).Normalize();
            RoofTypeName ??= string.Empty;
            WorksetName ??= string.Empty;
            IncludedLinks ??= new List<string>();
            if (string.IsNullOrWhiteSpace(ReportFolder)) ReportFolder = DefaultReportFolder;
            return this;
        }
    }
}
