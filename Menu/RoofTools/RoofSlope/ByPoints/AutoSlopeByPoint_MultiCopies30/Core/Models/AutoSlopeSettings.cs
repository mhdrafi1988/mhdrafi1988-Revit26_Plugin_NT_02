// =======================================================
// File: AutoSlopeSettings.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.MultiCopies30
// Changes vs V028:
//   - Single SlopePercent replaced with SlopeRows: 4 fixed rows
//     (IsEnabled + Percent), matching the Multi-Slope Roof Variants
//     UI. Persisted/loaded as a 4-element list; a missing/short list
//     on load is padded with disabled default rows so old or
//     corrupt settings never crash the window.
// Purpose: Plain POCO persisted to
//          %AppData%\Revit26_Plugin_Revised\AutoSlopeByPoint_MultiCopies28\settings.json
//          via System.Text.Json. Kept in a separate file/folder from
//          V028's settings.json since the schema is incompatible.
// Scope: Line Style is persisted by NAME (not ElementId) since the Id
//        is project-specific and not portable across documents — the
//        name is re-matched against LineStyleOptions on load; if no
//        match is found the constructor's default line style applies.
// =======================================================

using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.MultiCopies30.Core.Models
{
    public class CircleMarkerGroupSettings
    {
        public bool IsEnabled { get; set; } = true;
        public string LineStyleName { get; set; }
        public string ColorName { get; set; } = "Black";
        public double RadiusMm { get; set; } = 250;
        public bool ShowOffsetText { get; set; } = true;
    }

    public class AutoSlopeSettings
    {
        // ── Circle Markers (existing) ────────────────────────────────────
        public CircleMarkerGroupSettings DrainMarkerGroup { get; set; } = new CircleMarkerGroupSettings();
        public CircleMarkerGroupSettings HighestPointMarkerGroup { get; set; } = new CircleMarkerGroupSettings();
        public CircleMarkerGroupSettings AllowedOffsetMarkerGroup { get; set; } = new CircleMarkerGroupSettings();
        public double AllowedOffsetThresholdMm { get; set; } = 500;

        // ── Run inputs ─────────────────────────────────────────────────────
        public List<SlopeRowSetting> SlopeRows { get; set; } = new List<SlopeRowSetting>
        {
            new SlopeRowSetting { RowIndex = 1, IsEnabled = true,  Percent = 1.0 },
            new SlopeRowSetting { RowIndex = 2, IsEnabled = false, Percent = 2.0 },
            new SlopeRowSetting { RowIndex = 3, IsEnabled = false, Percent = 3.0 },
            new SlopeRowSetting { RowIndex = 4, IsEnabled = false, Percent = 4.0 },
        };
        public int ThresholdMeters { get; set; } = 1;
        public bool EnableDrainTolerance { get; set; } = true;
        public int DrainToleranceMm { get; set; } = 50;
        public bool InsertCurveIntersectionPoints { get; set; } = true;

        // ── Export (new in V026) ─────────────────────────────────────────
        public string ExportFolderPath { get; set; }
        public bool ExportToExcel { get; set; } = true;
        public bool AskToOpenAfterExport { get; set; } = false;
    }
}
