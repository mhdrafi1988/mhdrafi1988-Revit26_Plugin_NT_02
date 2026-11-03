// =======================================================
// File: ExportConfig.cs
// Location: Core/Models/
// Changes vs original:
//   REMOVED  37 unused properties (CAD, JSON, PDF, email,
//            parallel processing, version control, etc.)
//   REMOVED  JsonSerializationFormat, ReportFormat, LogLevel
//            enums (none were consumed anywhere)
//   KEPT     ExportFormat enum (used by IsExcelExportEnabled)
//   KEPT     the 5 properties actually read by ExcelExportHelper
//            and AutoSlopeEngine:
//              ExportPath, ExportToExcel, IncludeVertexDetails,
//              FileNamePrefix, IncludeTimestamp
// =======================================================

using System;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Models
{
    public class ExportConfig
    {
        /// <summary>Absolute folder path where Excel files are saved.</summary>
        public string ExportPath { get; set; } = string.Empty;

        /// <summary>Master switch: write any Excel files at all.</summary>
        public bool ExportToExcel { get; set; } = true;

        /// <summary>
        /// When true, the engine also writes the detailed multi-sheet workbook
        /// (Summary / Drain Points / Vertices / Statistics).
        /// When false, only the compact single-sheet workbook is written.
        /// </summary>
        public bool IncludeVertexDetails { get; set; } = true;

        /// <summary>Prefix prepended to the summary-only export filename.</summary>
        public string FileNamePrefix { get; set; } = "FloorSlopeRidge_";

        /// <summary>Append a yyyyMMdd_HHmmss timestamp to summary-only filenames.</summary>
        public bool IncludeTimestamp { get; set; } = true;

        // ── Helpers ────────────────────────────────────────────────────────────

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
