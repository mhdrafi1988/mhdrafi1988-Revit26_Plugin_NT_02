// =======================================================
// File: AutoSlopeParameterWriter.cs
// Fixes:
//   #6  WriteAll now accepts a runDate string instead of
//       calling DateTime.Now independently. The engine
//       captures the date once and passes it here, so the
//       parameter value in Revit always matches the value
//       shown in the UI.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Infrastructure.Helpers;
using System;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Parameters
{
    public static class AutoSlopeParameterWriter
    {
        // Fix #6: runDate parameter added — no longer calls DateTime.Now internally
        // Now returns the computed status code (1=OK, 2=Partial, 3=Failed) so
        // callers (e.g. AutoSlopeEngine) can pass it through to AutoSlopeResult
        // for Excel export without duplicating the success/fail counting logic.
        public static int WriteAll(
            Document doc,
            Floor floor,
            AutoSlopePayload data,
            double highestElevation_mm,
            double longestPath_m,
            int processed,
            int skipped,
            int runDuration_sec,
            int finalDrainCount,
            string runDate,            // ← new: supplied by AutoSlopeEngine
            string version = "P.10.00")
        {
            if (doc == null || floor == null)
                return AppConstants.Status_Failed;

            int successCount = 0;
            int failCount    = 0;
            int statusValue;

            using (Transaction tx = new Transaction(doc, "AutoSlope – Update Floor Parameters"))
            {
                tx.Start();

                TrySetDouble(floor,
                    AppConstants.Param_HighestElevation,
                    UnitUtils.ConvertToInternalUnits(highestElevation_mm, UnitTypeId.Millimeters),
                    ref successCount, ref failCount);

                TrySetInt(floor,
                    AppConstants.Param_VerticesProcessed,
                    processed,
                    ref successCount, ref failCount);

                TrySetInt(floor,
                    AppConstants.Param_VerticesSkipped,
                    skipped,
                    ref successCount, ref failCount);

                TrySetInt(floor,
                    AppConstants.Param_DrainCount,
                    finalDrainCount,
                    ref successCount, ref failCount);

                TrySetInt(floor,
                    AppConstants.Param_RunDuration,
                    runDuration_sec,
                    ref successCount, ref failCount);

                TrySetDouble(floor,
                    AppConstants.Param_LongestPath,
                    longestPath_m,
                    ref successCount, ref failCount);

                // Standardized (2026-09): store the raw slope percent (not divided by
                // 100), matching the ByDrain tools and the shared-parameters spec.
                TrySetDouble(floor,
                    AppConstants.Param_SlopePercent,
                    data.SlopePercent,
                    ref successCount, ref failCount);

                TrySetString(floor,
                    AppConstants.Param_SlopePercent_Text,
                    $"{data.SlopePercent}%",
                    ref successCount, ref failCount);

                // Standardized (2026-09): Threshold now stored in Revit internal units
                // (feet) via UnitUtils, instead of millimeters.
                TrySetDouble(floor,
                    AppConstants.Param_Threshold,
                    UnitUtils.ConvertToInternalUnits(data.ThresholdMeters, UnitTypeId.Meters),
                    ref successCount, ref failCount);

                // Fix #6: use the runDate passed in rather than a second DateTime.Now
                TrySetString(floor,
                    AppConstants.Param_RunDate,
                    runDate,
                    ref successCount, ref failCount);

                TrySetString(floor,
                    AppConstants.Param_Versions,
                    version,
                    ref successCount, ref failCount);

                // Kept as a plain millimeter value per Rafi's confirmed decision
                // (2026-09) — DrainToleranceMm stays mm, not internal units.
                TrySetInt(floor,
                    AppConstants.Param_DrainToleranceMm,
                    data.EnableDrainTolerance ? (int)Math.Round((double)data.DrainToleranceMm) : 0,
                    ref successCount, ref failCount);

                TrySetInt(floor,
                    AppConstants.Param_DrainToleranceEnabled,
                    data.EnableDrainTolerance ? 1 : 0,
                    ref successCount, ref failCount);

                statusValue = successCount == 0
                    ? AppConstants.Status_Failed
                    : failCount > 0
                        ? AppConstants.Status_Partial
                        : AppConstants.Status_OK;

                TrySetInt(floor,
                    AppConstants.Param_Status,
                    statusValue,
                    ref successCount, ref failCount);

                tx.Commit();
            }

            data?.Log?.Invoke(new Revit26_Plugin_Revised.Shared.Models.LogEntry(
                Revit26_Plugin_Revised.Shared.Models.LogLevel.Info,
                $"AutoSlope Parameters: {successCount} updated, {failCount} skipped"));

            return statusValue;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static void TrySetInt(
            Element elem, string paramName, int value,
            ref int ok, ref int fail)
        {
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer)
                { fail++; return; }
                p.Set(value);
                ok++;
            }
            catch { fail++; }
        }

        private static void TrySetDouble(
            Element elem, string paramName, double value,
            ref int ok, ref int fail)
        {
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double)
                { fail++; return; }
                p.Set(value);
                ok++;
            }
            catch { fail++; }
        }

        private static void TrySetString(
            Element elem, string paramName, string value,
            ref int ok, ref int fail)
        {
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                { fail++; return; }
                p.Set(value);
                ok++;
            }
            catch { fail++; }
        }
    }
}
