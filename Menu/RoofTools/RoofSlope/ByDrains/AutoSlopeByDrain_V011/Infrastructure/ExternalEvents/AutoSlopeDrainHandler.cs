// File: AutoSlopeDrainHandler.cs
// Location: Infrastructure/ExternalEvents/
// Base: ported unchanged from AutoSlopeByDrain V004.
//
// UPDATED (V008), per Rafi's confirmed multi-roof decision (2026-09-08):
// Payload (single AutoSlopeDrainPayload) replaced with MultiPayload
// (AutoSlopeDrainMultiPayload, carrying one AutoSlopeDrainPayload per roof).
// Every roof in the batch runs inside the SAME TransactionGroup, so the
// whole multi-roof run is one combined Undo entry. Each roof's own
// Transaction (opened inside AutoSlopeDrainEngine/RoofSlopeProcessorService)
// still commits or rolls back independently — a failure on one roof does not
// touch another roof's already-committed changes; it only means the group is
// Assimilate()'d instead of RollBack()'d as long as AT LEAST ONE roof
// succeeded, since RollBack() on a TransactionGroup would undo every
// sub-transaction in it, including roofs that already succeeded.
// After every roof has run, the optional combined summary workbook is
// written (ExcelExportService.ExportCombinedSummary), then OnAllCompleted
// fires once with every roof's result.

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Core.Engine;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Core.Models;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Infrastructure.ExternalEvents
{
    public class AutoSlopeDrainHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static AutoSlopeDrainMultiPayload MultiPayload;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (MultiPayload == null) return;

            AutoSlopeDrainMultiPayload current = MultiPayload;
            var roofResults = new List<AutoSlopeDrainRoofResult>();
            bool anySuccess = false;

            using (TransactionGroup tg = new TransactionGroup(
                app.ActiveUIDocument.Document, "AutoSlope By Drain"))
            {
                tg.Start();

                foreach (var roofPayload in current.RoofPayloads ?? new List<AutoSlopeDrainPayload>())
                {
                    AutoSlopeDrainResult result;
                    try
                    {
                        result = AutoSlopeDrainEngine.Execute(app, roofPayload);
                    }
                    catch (Exception ex)
                    {
                        roofPayload.Log?.Invoke(new LogEntry(LogLevel.Error,
                            $"[AutoSlopeDrainHandler] Unhandled exception: {ex.Message}"));
                        result = new AutoSlopeDrainResult
                        {
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                    }

                    if (result.Success) anySuccess = true;

                    var roofResult = new AutoSlopeDrainRoofResult
                    {
                        RoofName = roofPayload.RoofName,
                        RoofElementId = roofPayload.RoofId?.Value ?? 0,
                        Result = result
                    };
                    roofResults.Add(roofResult);

                    roofPayload.OnCompleted?.Invoke(result);

                    // NEW (V010), per Rafi's confirmed Cancel-scope decision: roofs
                    // already committed above stay committed (still folded into the
                    // Assimilate()/RollBack() call below); the cancelled roof's own
                    // transaction already rolled back a true no-op inside
                    // AutoSlopeDrainEngine/RoofSlopeProcessorService. Remaining roofs
                    // in the batch are never started.
                    if (result.WasCancelled)
                    {
                        current.Log?.Invoke(new LogEntry(LogLevel.Warning,
                            "Run cancelled — remaining roof(s) in this batch were not started."));
                        break;
                    }
                }

                if (anySuccess)
                    tg.Assimilate();
                else
                    tg.RollBack();
            }

            if (current.WriteCombinedSummary && roofResults.Count > 1)
            {
                string combinedPath = ExcelExportService.ExportCombinedSummary(
                    roofResults, current.ExportFolderPath, current.ProjectTitle, current.Log);

                if (!string.IsNullOrEmpty(combinedPath))
                    current.Log?.Invoke(new LogEntry(LogLevel.Success, $"✅ Combined multi-roof summary exported to: {combinedPath}"));
            }

            current.OnAllCompleted?.Invoke(roofResults);
        }

        public string GetName() => "AutoSlope By Drain Handler";
    }
}
