// File: ExcelExportService.cs
// Location: Infrastructure/Helpers/
// Ported from AutoSlopeByPoint's ExcelExportService pattern — checks
// EPPlus.dll is actually present in the plugin folder before attempting to
// use it (so a missing dependency logs a clear Warning instead of an
// unhandled exception), then delegates to ExcelExportHelper.

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers
{
    public static class ExcelExportService
    {
        private static bool? _isAvailable = null;

        private static bool IsAvailable(Action<LogEntry> log)
        {
            if (_isAvailable.HasValue) return _isAvailable.Value;

            string pluginFolder = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string epPlusPath = Path.Combine(pluginFolder, "EPPlus.dll");

            _isAvailable = File.Exists(epPlusPath);

            if (!_isAvailable.Value)
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"⚠ Excel export skipped: EPPlus.dll not found in plugin folder. Expected at: {epPlusPath}"));

            return _isAvailable.Value;
        }

        public static string ExportWorkbook(
            AutoSlopeDrainPayload payload,
            List<DrainVertexData> vertexData,
            Floor floor,
            double slopePercent,
            DrainExportMetrics metrics,
            Action<LogEntry> log = null)
        {
            if (!IsAvailable(log ?? payload?.Log)) return null;
            try
            {
                return ExcelExportHelper.ExportWorkbook(
                    payload, vertexData, floor, slopePercent, metrics, metrics?.Version ?? Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine.AutoSlopeDrainEngine.FallbackToolVersion);
            }
            catch (Exception ex)
            {
                (log ?? payload?.Log)?.Invoke(new LogEntry(LogLevel.Error,
                    $"⚠ Excel export error: {ex.Message}"));
                return null;
            }
        }

        /// <summary>
        /// NEW (V008). Writes one extra workbook comparing every floor in a
        /// multi-floor run — one row per floor (name, status, drain count,
        /// longest path, highest elevation, its own exported file). Called
        /// once per batch from AutoSlopeDrainHandler, after every floor's own
        /// per-floor workbook has already been written.
        /// </summary>
        public static string ExportCombinedSummary(
            List<AutoSlopeDrainFloorResult> floorResults,
            string exportFolderPath,
            string projectTitle,
            Action<LogEntry> log = null)
        {
            if (!IsAvailable(log)) return null;
            try
            {
                return ExcelExportHelper.ExportCombinedSummary(floorResults, exportFolderPath, projectTitle);
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Error,
                    $"⚠ Combined summary export error: {ex.Message}"));
                return null;
            }
        }
    }
}
