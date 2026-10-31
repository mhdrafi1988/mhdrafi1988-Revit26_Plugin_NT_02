// =======================================================
// File: AutoSlopeSettings.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models
// Changes vs V030:
//   - Every default comes from AutoSlopeConstants (V030 B2: this POCO's own
//     defaults — 1 m threshold, 50 mm tolerance, Black markers — overrode
//     the real defaults on a clean machine).
//   - ThresholdMeters (int) split into MaxDrainDistanceM + MaxEdgeLengthM
//     (double) — V030 B3.
//   - New: DrainMode, IncludeNearbyPoints (V032 D2).
//   - V032: remembers which option sections were left open.
//   - 32.3.0: ShowCornerOffsetText on the marker group (default off).
//   - Saved to settings.v032.json so V030 (kept as fallback) and V032 never
//     read each other's file.
//   - V033: ridge lines (DrawRidgeLines, off by default; RidgeLineStyleName)
//     and their section's open state. Saved to settings.v033.json; the
//     first time, V033 reads V032's file, where these fields are missing
//     and read as off (old behaviour).
// Line styles are persisted by NAME (ElementIds are per project).
// =======================================================

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models
{
    public class CircleMarkerGroupSettings
    {
        public bool IsEnabled { get; set; } = true;
        public string LineStyleName { get; set; }
        public string ColorName { get; set; }
        public double RadiusMm { get; set; } = AutoSlopeConstants.DefaultMarkerRadiusMm;
        public bool ShowOffsetText { get; set; } = true;

        /// <summary>32.3.0: also place the Offset text note at every outer roof boundary corner.
        /// Missing in older settings files, so it reads as false (old behaviour).</summary>
        public bool ShowCornerOffsetText { get; set; }
    }

    public class AutoSlopeSettings
    {
        // ── Circle markers ───────────────────────────────────────────────
        public CircleMarkerGroupSettings DrainMarkerGroup { get; set; } =
            new CircleMarkerGroupSettings { ColorName = AutoSlopeConstants.DefaultDrainColor };

        public CircleMarkerGroupSettings HighestPointMarkerGroup { get; set; } =
            new CircleMarkerGroupSettings { ColorName = AutoSlopeConstants.DefaultHighestColor };

        public CircleMarkerGroupSettings AllowedOffsetMarkerGroup { get; set; } =
            new CircleMarkerGroupSettings { ColorName = AutoSlopeConstants.DefaultOffsetColor };

        public double AllowedOffsetThresholdMm { get; set; } = AutoSlopeConstants.DefaultAllowedOffsetThresholdMm;

        // ── Run inputs ───────────────────────────────────────────────────
        public double SlopePercent { get; set; } = AutoSlopeConstants.DefaultSlopePercent;
        public double MaxDrainDistanceM { get; set; } = AutoSlopeConstants.DefaultMaxDrainDistanceM;
        public double MaxEdgeLengthM { get; set; } = AutoSlopeConstants.DefaultMaxEdgeLengthM;
        public DrainMode DrainMode { get; set; } = DrainMode.SnapToNearestPoint;
        public bool IncludeNearbyPoints { get; set; } = true;
        public double SnapRadiusMm { get; set; } = AutoSlopeConstants.DefaultSnapRadiusMm;
        public bool InsertCurveIntersectionPoints { get; set; } = true;

        // ── Ridge lines (V033) ─────────────────────────────────────────────
        /// <summary>Draw detail lines along the ridges between drain groups. Off by default.</summary>
        public bool DrawRidgeLines { get; set; }

        /// <summary>Line style for the ridge lines, by name. Null = <see cref="AutoSlopeConstants.DefaultRidgeLineStyleName"/>.</summary>
        public string RidgeLineStyleName { get; set; }

        // ── Export ───────────────────────────────────────────────────────
        public string ExportFolderPath { get; set; }
        public bool ExportToExcel { get; set; } = true;
        public bool AskToOpenAfterExport { get; set; }

        // ── Window (V032): which option sections were left open ─────────
        public bool DrainOptionsExpanded { get; set; }
        public bool PathOptionsExpanded { get; set; }
        public bool MarkersExpanded { get; set; }
        public bool ExportExpanded { get; set; }
        public bool RidgeLinesExpanded { get; set; }
    }
}
