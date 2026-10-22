// =======================================================
// File: AutoSlopeSettings.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models
// Changes vs V030:
//   - Every default comes from AutoSlopeConstants (V030 B2: this POCO's own
//     defaults — 1 m threshold, 50 mm tolerance, Black markers — overrode
//     the real defaults on a clean machine).
//   - ThresholdMeters (int) split into MaxDrainDistanceM + MaxEdgeLengthM
//     (double) — V030 B3.
//   - New: DrainMode, IncludeNearbyPoints (V032 D2).
//   - V032: remembers which option sections were left open.
//   - Saved to settings.v032.json so V030 (kept as fallback) and V032 never
//     read each other's file.
// Line styles are persisted by NAME (ElementIds are per project).
// =======================================================

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models
{
    public class CircleMarkerGroupSettings
    {
        public bool IsEnabled { get; set; } = true;
        public string LineStyleName { get; set; }
        public string ColorName { get; set; }
        public double RadiusMm { get; set; } = AutoSlopeConstants.DefaultMarkerRadiusMm;
        public bool ShowOffsetText { get; set; } = true;
        public bool LabelCornerPoints { get; set; }
        public bool LabelAllPoints { get; set; }
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

        // ── Export ───────────────────────────────────────────────────────
        public string ExportFolderPath { get; set; }
        public bool ExportToExcel { get; set; } = true;
        public bool AskToOpenAfterExport { get; set; }

        // ── Window (V032): which option sections were left open ─────────
        public bool DrainOptionsExpanded { get; set; }
        public bool PathOptionsExpanded { get; set; }
        public bool MarkersExpanded { get; set; }
        public bool ExportExpanded { get; set; }
    }
}
