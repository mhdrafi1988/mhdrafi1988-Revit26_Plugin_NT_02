// File: ExportConfig.cs
// Location: Core/Models/
//
// Changes vs ByPoint V018 / ByDrain V004:
//   REMOVED  IncludeVertexDetails — in ByPoint this flag actually did two
//            things: (1) gated whether skipped vertices were added to the
//            Vertex Data sheet, and (2) triggered a SECOND, duplicate
//            Excel export call (ExportDetailedVertexData) that wrote an
//            identical file under a second filename, logged with a stale
//            "Sheets: Summary, Drain Points, Vertices, Statistics" message
//            describing sheets that were never actually created. This was
//            a bug, not intentional behavior — confirmed with Rafi and
//            removed. The combined tool always includes skipped vertices
//            in the single Vertex Data sheet and makes exactly one export
//            call. See AutoSlopeDrainEngine / ExcelExportHelper.
//   REMOVED  ExportToCsv, FileNamePrefix pattern from ByDrain V004 — export
//            is Excel-only per Rafi's confirmed decision (2026-07-21).
//   KEPT     ExportPath, IncludeTimestamp — same meaning as both prior tools.

using System;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models
{
    public class ExportConfig
    {
        /// <summary>Absolute folder path where the Excel file is saved.</summary>
        public string ExportPath { get; set; } = string.Empty;

        /// <summary>Master switch: write the Excel workbook at all.</summary>
        public bool ExportToExcel { get; set; } = true;

        /// <summary>Prefix prepended to the export filename.</summary>
        public string FileNamePrefix { get; set; } = "FloorSlopeByDrain_";

        /// <summary>Append a yyyyMMdd_HHmmss timestamp to the filename.</summary>
        public bool IncludeTimestamp { get; set; } = true;

        // ── Helpers ──────────────────────────────────────────────────────────

        public bool IsValid() => !string.IsNullOrWhiteSpace(ExportPath);

        public string GetDefaultFileName()
        {
            string timestamp = IncludeTimestamp
                ? $"_{DateTime.Now:yyyyMMdd_HHmmss}"
                : string.Empty;
            return $"{FileNamePrefix}{timestamp}";
        }
    }
}
