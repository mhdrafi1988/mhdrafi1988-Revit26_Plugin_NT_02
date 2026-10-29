// =======================================================
// File: AutoSlopeParameterWriter.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// Moved from V030 Core/Parameters (Revit API). Same parameters and units
// as V030 (confirmed 2026-09 standardisation kept):
//   - slope stored as the raw percent
//   - threshold (now the max drainage distance) in internal units
//   - drain tolerance stays plain mm (per Rafi, 2026-09)
// Changes: version comes from ToolCatalog (B6); longest path argument is
// named for what it is — internal feet (V030 called it "_m").
// =======================================================

using System;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    internal sealed class ParameterWriteOutcome
    {
        public int Status { get; set; }
        public int Written { get; set; }
        public int Skipped { get; set; }
    }

    internal static class AutoSlopeParameterWriter
    {
        public static ParameterWriteOutcome WriteAll(
            Document doc,
            RoofBase roof,
            AutoSlopeRequest request,
            double highestElevationMm,
            double longestPathFt,
            int processed,
            int skipped,
            int durationSec,
            int finalDrainCount,
            string runDate,
            string version)
        {
            var o = new ParameterWriteOutcome { Status = AutoSlopeConstants.Status_Failed };
            if (doc == null || roof == null) return o;

            int ok = 0, fail = 0;
            using (var tx = new Transaction(doc, "AutoSlope – Update Roof Parameters"))
            {
                tx.Start();

                SetDouble(roof, AutoSlopeConstants.Param_HighestElevation, RevitConvert.MmToFt(highestElevationMm), ref ok, ref fail);
                SetInt(roof, AutoSlopeConstants.Param_VerticesProcessed, processed, ref ok, ref fail);
                SetInt(roof, AutoSlopeConstants.Param_VerticesSkipped, skipped, ref ok, ref fail);
                SetInt(roof, AutoSlopeConstants.Param_DrainCount, finalDrainCount, ref ok, ref fail);
                SetInt(roof, AutoSlopeConstants.Param_RunDuration, durationSec, ref ok, ref fail);
                SetDouble(roof, AutoSlopeConstants.Param_LongestPath, longestPathFt, ref ok, ref fail);
                SetDouble(roof, AutoSlopeConstants.Param_SlopePercent, request.SlopePercent, ref ok, ref fail);
                SetString(roof, AutoSlopeConstants.Param_SlopePercent_Text, $"{request.SlopePercent}%", ref ok, ref fail);
                SetDouble(roof, AutoSlopeConstants.Param_Threshold, RevitConvert.MToFt(request.MaxDrainDistanceM), ref ok, ref fail);
                SetString(roof, AutoSlopeConstants.Param_RunDate, runDate, ref ok, ref fail);
                SetString(roof, AutoSlopeConstants.Param_Versions, version, ref ok, ref fail);
                SetInt(roof, AutoSlopeConstants.Param_DrainToleranceMm,
                    request.IncludeNearbyPoints ? (int)Math.Round(request.SnapRadiusMm) : 0, ref ok, ref fail);
                SetInt(roof, AutoSlopeConstants.Param_DrainToleranceEnabled, request.IncludeNearbyPoints ? 1 : 0, ref ok, ref fail);

                o.Status = ok == 0
                    ? AutoSlopeConstants.Status_Failed
                    : fail > 0 ? AutoSlopeConstants.Status_Partial : AutoSlopeConstants.Status_OK;

                SetInt(roof, AutoSlopeConstants.Param_Status, o.Status, ref ok, ref fail);
                tx.Commit();
            }

            o.Written = ok;
            o.Skipped = fail;
            return o;
        }

        private static void SetInt(Element e, string name, int value, ref int ok, ref int fail)
        {
            try
            {
                Parameter p = e.LookupParameter(name);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) { fail++; return; }
                p.Set(value);
                ok++;
            }
            catch { fail++; }
        }

        private static void SetDouble(Element e, string name, double value, ref int ok, ref int fail)
        {
            try
            {
                Parameter p = e.LookupParameter(name);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) { fail++; return; }
                p.Set(value);
                ok++;
            }
            catch { fail++; }
        }

        private static void SetString(Element e, string name, string value, ref int ok, ref int fail)
        {
            try
            {
                Parameter p = e.LookupParameter(name);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) { fail++; return; }
                p.Set(value);
                ok++;
            }
            catch { fail++; }
        }
    }
}
