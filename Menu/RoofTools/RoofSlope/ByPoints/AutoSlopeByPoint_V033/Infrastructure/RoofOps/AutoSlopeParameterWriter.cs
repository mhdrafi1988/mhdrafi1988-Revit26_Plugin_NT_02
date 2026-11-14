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
        /// <summary>One line per parameter that could not be written, with the reason.</summary>
        public System.Collections.Generic.List<string> Failures { get; } = new System.Collections.Generic.List<string>();
    }

    internal static class AutoSlopeParameterWriter
    {
        /// <summary>
        /// Writes the run summary to the roof's AutoSlope_* parameters. Each value is converted to the
        /// parameter's own storage type; every parameter that cannot be written is listed with the reason.
        /// </summary>
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

            using (var tx = new Transaction(doc, "AutoSlope – Update Roof Parameters"))
            {
                tx.Start();

                Set(roof, AutoSlopeConstants.Param_HighestElevation, RevitConvert.MmToFt(highestElevationMm), o);
                Set(roof, AutoSlopeConstants.Param_VerticesProcessed, processed, o);
                Set(roof, AutoSlopeConstants.Param_VerticesSkipped, skipped, o);
                Set(roof, AutoSlopeConstants.Param_DrainCount, finalDrainCount, o);
                Set(roof, AutoSlopeConstants.Param_RunDuration, durationSec, o);
                Set(roof, AutoSlopeConstants.Param_LongestPath, longestPathFt, o);
                Set(roof, AutoSlopeConstants.Param_SlopePercent, request.SlopePercent, o);
                Set(roof, AutoSlopeConstants.Param_SlopePercent_Text, $"{request.SlopePercent}%", o);
                Set(roof, AutoSlopeConstants.Param_Threshold, RevitConvert.MToFt(request.MaxDrainDistanceM), o);
                Set(roof, AutoSlopeConstants.Param_RunDate, runDate, o);
                Set(roof, AutoSlopeConstants.Param_Versions, version, o);
                Set(roof, AutoSlopeConstants.Param_DrainToleranceMm,
                    request.IncludeNearbyPoints ? (int)Math.Round(request.SnapRadiusMm) : 0, o);
                Set(roof, AutoSlopeConstants.Param_DrainToleranceEnabled, request.IncludeNearbyPoints ? 1 : 0, o);

                o.Status = o.Written == 0
                    ? AutoSlopeConstants.Status_Failed
                    : o.Skipped > 0 ? AutoSlopeConstants.Status_Partial : AutoSlopeConstants.Status_OK;

                Set(roof, AutoSlopeConstants.Param_Status, o.Status, o);
                tx.Commit();
            }

            return o;
        }

        /// <summary>Sets one parameter, converting the value to its storage type. Records the reason on failure.</summary>
        private static void Set(Element e, string name, object value, ParameterWriteOutcome o)
        {
            try
            {
                Parameter p = e.LookupParameter(name);
                if (p == null) { Fail(o, name, "not found on the roof (is the shared parameter bound to Roofs?)"); return; }
                if (p.IsReadOnly) { Fail(o, name, "read-only"); return; }

                bool done;
                switch (p.StorageType)
                {
                    case StorageType.Integer:
                        done = p.Set(Convert.ToInt32(Math.Round(Convert.ToDouble(value)))); break;
                    case StorageType.Double:
                        done = p.Set(Convert.ToDouble(value)); break;
                    case StorageType.String:
                        done = p.Set(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)); break;
                    default:
                        Fail(o, name, $"unsupported data type {p.StorageType}"); return;
                }

                if (done) o.Written++; else Fail(o, name, "Revit refused the value");
            }
            catch (Exception ex)
            {
                Fail(o, name, ex.Message);
            }
        }

        private static void Fail(ParameterWriteOutcome o, string name, string reason)
        {
            o.Skipped++;
            o.Failures.Add($"{name}: {reason}");
        }
    }
}
