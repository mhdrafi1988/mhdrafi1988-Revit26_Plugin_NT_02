// File: AutoSlopeDrainParameterWriter.cs
// Location: Core/Parameters/
// Base: ported from AutoSlopeByDrain V004 (roof).
//
// BUG FIXES (flagged during audit, not silently changed):
//   1. Param_Threshold unit inconsistency — ByPoint's writer stored
//      data.ThresholdMeters * 1000.0 (i.e. millimeters) into this parameter;
//      V004's writer stored the raw meters value into the SAME parameter
//      name. Since both tools share this parameter on the same floor
//      instance, this was a real inconsistency depending on which tool last
//      wrote it. FIXED: stores meters consistently (matches V004's own
//      in-code comment "// Store in meters"), and the docstring makes the
//      unit explicit.
//   2. Version string was hardcoded "V004" inline instead of passed as a
//      parameter — meant every future version bump required editing this
//      file directly. FIXED: version is now a required parameter, sourced
//      from AutoSlopeDrainResult.Version ("006" default, confirmed by Rafi).

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using System;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Parameters
{
    public class AutoSlopeDrainParameterWriter : IParameterWriter
    {
        public ParameterWriteResult WriteAll(
            Document doc,
            Floor floor,
            DrainExportMetrics metrics,
            double slopePercent,
            double thresholdMeters,
            Action<string> logAction = null)
        {
            return WriteAll(doc, floor, metrics, slopePercent, thresholdMeters, metrics?.Version ?? Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine.AutoSlopeDrainEngine.FallbackToolVersion, logAction);
        }

        /// <summary>
        /// Overload accepting an explicit version string. The interface-required
        /// overload above falls back to metrics.Version so existing call sites
        /// that only know IParameterWriter still work.
        /// </summary>
        public ParameterWriteResult WriteAll(
            Document doc,
            Floor floor,
            DrainExportMetrics metrics,
            double slopePercent,
            double thresholdMeters,
            string version,
            Action<string> logAction = null)
        {
            if (doc == null || floor == null)
                return new ParameterWriteResult();

            int successCount = 0;
            int failCount = 0;

            using (Transaction tx = new Transaction(doc, "Floor Slope – Update Floor Parameters"))
            {
                tx.Start();

                // Standardized (2026-09): HighestElevation now stored in Revit internal
                // units (feet) via UnitUtils, matching the ByPoints tools — was
                // previously an Integer (rounded mm), which mismatched the Length-typed
                // shared parameter and silently failed to write.
                TrySetDouble(floor, AppConstants.Param_HighestElevation,
                    UnitUtils.ConvertToInternalUnits(metrics.HighestElevationMm, UnitTypeId.Millimeters),
                    ref successCount, ref failCount);

                // Integer parameters
                TrySetInt(floor, AppConstants.Param_VerticesProcessed,
                    metrics.ProcessedVertices,
                    ref successCount, ref failCount);

                TrySetInt(floor, AppConstants.Param_VerticesSkipped,
                    metrics.SkippedVertices,
                    ref successCount, ref failCount);

                TrySetInt(floor, AppConstants.Param_DrainCount,
                    metrics.DrainCount,
                    ref successCount, ref failCount);

                TrySetInt(floor, AppConstants.Param_RunDuration,
                    metrics.RunDurationSec,
                    ref successCount, ref failCount);

                // Double parameters
                TrySetDouble(floor, AppConstants.Param_LongestPath,
                    metrics.LongestPathM, // stored in meters
                    ref successCount, ref failCount);

                TrySetDouble(floor, AppConstants.Param_SlopePercent,
                    slopePercent,
                    ref successCount, ref failCount);

                // Standardized (2026-09): Threshold now stored in Revit internal units
                // (feet) via UnitUtils, matching the ByPoints tools and the shared-
                // parameters spec, instead of a raw meters double.
                TrySetDouble(floor, AppConstants.Param_Threshold,
                    UnitUtils.ConvertToInternalUnits(thresholdMeters, UnitTypeId.Meters),
                    ref successCount, ref failCount);

                // Standardized (2026-09): slope percent as display text, matching the
                // ByPoints tools. ByDrain has no separate "raw vs display" percent —
                // the same slopePercent value used above is formatted here.
                TrySetString(floor, AppConstants.Param_SlopePercent_Text,
                    $"{slopePercent}%",
                    ref successCount, ref failCount);

                // String parameters
                TrySetString(floor, AppConstants.Param_RunDate,
                    metrics.RunDate ?? DateTime.Now.ToString("dd-MM-yy HH:mm"),
                    ref successCount, ref failCount);

                // FIX: version now passed in rather than hardcoded.
                TrySetString(floor, AppConstants.Param_Versions,
                    version ?? Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine.AutoSlopeDrainEngine.FallbackToolVersion,
                    ref successCount, ref failCount);

                // Standardized (2026-09): AutoSlope_DrainToleranceMm added for parity
                // with the ByPoints tools' shared-parameter set. ByDrain has no
                // pick-point tolerance concept (drains are auto-detected from floor
                // geometry, not picked), so this always writes 0. Kept as a plain
                // millimeter Integer per Rafi's confirmed decision (2026-09).
                TrySetInt(floor, AppConstants.Param_DrainToleranceMm,
                    0,
                    ref successCount, ref failCount);

                // AutoSlope_DrainToleranceEnabled (shared-parameter file): ByDrain has no
                // pick-point tolerance matching, so it is always written as 0 (= no).
                TrySetInt(floor, AppConstants.Param_DrainToleranceEnabled,
                    0,
                    ref successCount, ref failCount);

                int statusValue = CalculateStatusValue(successCount, failCount);
                TrySetInt(floor, AppConstants.Param_Status, statusValue, ref successCount, ref failCount);

                tx.Commit();
            }

            string logMessage = $"AutoSlope parameters: {successCount} updated, {failCount} skipped";
            if (successCount == 0 && failCount > 0)
                logMessage += " (none of the AutoSlope_* shared parameters is on this floor: bind them to the Floors category to record run results; the slope itself is applied either way)";
            logAction?.Invoke(logMessage);

            return new ParameterWriteResult
            {
                SuccessCount = successCount,
                FailCount = failCount
            };
        }

        private int CalculateStatusValue(int successCount, int failCount)
        {
            if (successCount == 0) return AppConstants.Status_Failed;
            if (failCount > 0) return AppConstants.Status_Partial;
            return AppConstants.Status_OK;
        }

        private void TrySetInt(Element elem, string paramName, int value,
            ref int success, ref int fail)
        {
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer)
                {
                    fail++;
                    return;
                }
                p.Set(value);
                success++;
            }
            catch
            {
                fail++;
            }
        }

        private void TrySetDouble(Element elem, string paramName, double value,
            ref int success, ref int fail)
        {
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double)
                {
                    fail++;
                    return;
                }
                p.Set(value);
                success++;
            }
            catch
            {
                fail++;
            }
        }

        private void TrySetString(Element elem, string paramName, string value,
            ref int success, ref int fail)
        {
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                {
                    fail++;
                    return;
                }
                p.Set(value);
                success++;
            }
            catch
            {
                fail++;
            }
        }
    }
}
