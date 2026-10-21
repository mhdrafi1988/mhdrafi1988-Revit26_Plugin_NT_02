// =======================================================
// File: AutoSlopeSettings.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V030
// Changes vs V025:
//   - Persistence scope expanded: previously only the 3 Circle Marker
//     groups + AllowedOffsetThresholdMm were saved. Now also persists
//     the run-input fields (SlopePercent, ThresholdMeters,
//     EnableDrainTolerance, DrainToleranceMm,
//     InsertCurveIntersectionPoints) and export fields
//     (ExportFolderPath, ExportToExcel, AskToOpenAfterExport) — every
//     user-changeable value/option on the window is now remembered
//     across sessions. Window size/position is explicitly OUT of
//     scope (not persisted), per Rafi's confirmation.
//   - CircleMarkerGroupSettings.RadiusMm default 500 → 250 (matches
//     new default in CircleMarkerGroup.cs / AutoSlopeViewModel.cs).
//   - New: AskToOpenAfterExport (bool, default false) — governs the
//     new "Ask to open file after export" checkbox.
// Purpose: Plain POCO persisted to
//          %AppData%\Revit26_Plugin_Revised\AutoSlopeByPoint\settings.json
//          via System.Text.Json.
// Scope: Line Style is persisted by NAME (not ElementId) since the Id
//        is project-specific and not portable across documents — the
//        name is re-matched against LineStyleOptions on load; if no
//        match is found the constructor's default line style applies.
// =======================================================

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V030.Core.Models
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

        // ── Run inputs (new in V026) ─────────────────────────────────────
        public double SlopePercent { get; set; } = 1.0;
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
