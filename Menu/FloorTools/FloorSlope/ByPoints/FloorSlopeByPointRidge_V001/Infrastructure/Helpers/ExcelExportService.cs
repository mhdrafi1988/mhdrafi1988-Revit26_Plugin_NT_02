// =======================================================
// File: ExcelExportService.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001
// Changes vs V06:
//   - payload.Log is now Action<LogEntry>; all log calls
//     emit new LogEntry(LogLevel.X, "...") instead of strings.
//   - ExportResultsSummary log parameter updated to
//     Action<LogEntry> for consistency.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Infrastructure.Helpers
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

        public static string ExportCompactVertexData(
            AutoSlopePayload payload,
            List<VertexData> vertexData,
            Floor floor,
            double slopePercent,
            string version = "P.10.00",
            int status = 1,
            int runDurationSec = 0,
            string runDate = null,
            string runStartTime = null,
            string runEndTime = null)
        {
            if (!IsAvailable(payload?.Log)) return null;
            try
            {
                return ExcelExportHelper.ExportCompactVertexData(
                    payload, vertexData, floor, slopePercent, version, status,
                    runDurationSec, runDate, runStartTime, runEndTime);
            }
            catch (Exception ex)
            {
                payload?.Log?.Invoke(new LogEntry(LogLevel.Error,
                    $"⚠ Compact Excel export error: {ex.Message}"));
                return null;
            }
        }

        public static string ExportDetailedVertexData(
            AutoSlopePayload payload,
            List<VertexData> vertexData,
            Floor floor,
            List<XYZ> drainPoints,
            double slopePercent)
        {
            if (!IsAvailable(payload?.Log)) return null;
            try
            {
                return ExcelExportHelper.ExportDetailedVertexData(
                    payload, vertexData, floor, drainPoints, slopePercent);
            }
            catch (Exception ex)
            {
                payload?.Log?.Invoke(new LogEntry(LogLevel.Error,
                    $"⚠ Detailed Excel export error: {ex.Message}"));
                return null;
            }
        }

        public static string ExportResultsSummary(
            string filePath,
            AutoSlopeResult result,
            double slopePercent,
            int thresholdMeters,
            bool enableDrainTolerance,
            int drainToleranceMm,
            string exportFolderPath,
            Action<LogEntry> log = null)
        {
            if (!IsAvailable(log)) return null;
            try
            {
                return ExcelExportHelper.ExportResultsSummary(
                    filePath, result, slopePercent,
                    thresholdMeters, enableDrainTolerance,
                    drainToleranceMm, exportFolderPath);
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Error,
                    $"⚠ Results Excel export error: {ex.Message}"));
                return null;
            }
        }
    }
}
