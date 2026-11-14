// =======================================================
// File: MultiSlopeVariantEngine.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// Purpose: Orchestrates the Multi-Slope Floor Variants feature.
//
// Transaction structure (standardized 2026-09 — AutoSlopeParameterWriter
// now owns its own Transaction, matching every other AutoSlope tool,
// which Revit only allows once nothing else is open on the document):
//   - ONE outer TransactionGroup per floor, so the whole run still
//     assimilates into a single Undo entry for the user.
//   - Inside it, ONE Transaction per active slope row: copy the source
//     floor (drain XY preserved via zero-translation copy), apply that
//     row's slope geometry, resolve/create its workset, assign
//     ELEM_PARTITION_PARAM, then commit that Transaction.
//   - AFTER that Transaction commits (nothing left open on the
//     document), AutoSlopeEngine.FinalizeAndExport(...) runs, which
//     opens AutoSlopeParameterWriter's own Transaction to write the
//     AutoSlope_* parameters, then runs Excel export.
//   - If a row's geometry Transaction fails, it is rolled back and
//     logged as an error, and FinalizeAndExport is skipped for that
//     row; already-committed rows for this same floor are NOT rolled
//     back — the loop continues to the next slope.
// =======================================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Engine
{
    public static class MultiSlopeVariantEngine
    {
        public static void Execute(UIApplication app, MultiSlopePayload data)
        {
            Document doc = app.ActiveUIDocument.Document;

            Floor sourceFloor = doc.GetElement(data.FloorId) as Floor;
            if (sourceFloor == null)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Error, "Source floor element not found. Aborting."));
                Abort(data, "Source floor element not found. Aborting.");
                return;
            }

            if (!doc.IsWorkshared)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Error,
                    "This document is not workshared — Multi-Slope Floor Variants requires worksets. Aborting."));
                Abort(data, "Document is not workshared.");
                return;
            }

            List<SlopeRowSetting> activeRows = (data.SlopeRows ?? new List<SlopeRowSetting>())
                .Where(r => r.IsEnabled && r.Percent > 0)
                .OrderBy(r => r.RowIndex)
                .ToList();

            if (activeRows.Count == 0)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Error,
                    "No active slope rows selected. Aborting."));
                Abort(data, "No active slope rows selected.");
                return;
            }

            data.Log?.Invoke(new LogEntry(LogLevel.Info,
                $"Starting Multi-Slope Floor Variants — source floor {sourceFloor.Id}, {activeRows.Count} active slope(s)."));

            var results = new List<MultiSlopeVariantResult>();
            bool wasCancelled = false;
            int totalActive = activeRows.Count;

            using (TransactionGroup tg = new TransactionGroup(doc, $"Multi-Slope Floor Variants — Floor {sourceFloor.Id}"))
            {
                tg.Start();

                for (int rowPos = 0; rowPos < activeRows.Count; rowPos++)
                {
                    SlopeRowSetting row = activeRows[rowPos];

                    try { ToolCancellation.CheckCurrent(); }
                    catch (OperationCanceledException) { wasCancelled = true; }
                    if (wasCancelled || data.CancelToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        break;
                    }

                    double rowWeightStart = (double)rowPos / totalActive * 100.0;
                    double rowWeightEnd = (double)(rowPos + 1) / totalActive * 100.0;

                    MultiSlopeVariantResult rowResult = ProcessRow(app, doc, sourceFloor, data, row,
                        pct => data.Progress?.Invoke(new RunProgressInfo(
                            $"Slope {row.Percent}%", rowWeightStart + (pct / 100.0) * (rowWeightEnd - rowWeightStart))));
                    results.Add(rowResult);

                    if (rowResult.WasCancelled)
                    {
                        wasCancelled = true;
                        break;
                    }
                }

                if (wasCancelled)
                {
                    // Undo-all: copies that had already committed are rolled back with the rest.
                    int exported = results.Count(r => !string.IsNullOrEmpty(r.EngineResult?.ExportedFilePath));
                    int done = results.Count(r => r.Success);
                    tg.RollBack();
                    foreach (var r in results.Where(r => r.Success))
                    {
                        r.Success = false;
                        r.WasCancelled = true;
                        r.ErrorMessage = "Rolled back: the run was cancelled.";
                    }
                    data.Log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"Cancelled: {done} of {totalActive} slope(s) had been created; all of them were undone. " +
                        $"{exported} Excel file(s) had already been written and are kept."));
                }
                else
                {
                    tg.Assimilate();
                }
            }

            int copiesCreated = results.Count(r => r.Success);
            int worksetsCreated = results.Count(r => r.Success && r.WorksetWasCreated);
            int failed = results.Count(r => !r.Success && !r.WasCancelled);

            data.Log?.Invoke(new LogEntry(
                failed == 0 ? LogLevel.Success : LogLevel.Warning,
                $"{copiesCreated} copies created | {worksetsCreated} worksets created | {failed} failed"));

            data.OnCompleted?.Invoke(new MultiSlopeRunSummary
            {
                Success = true,
                WasCancelled = wasCancelled,
                Results = results,
                CopiesCreated = copiesCreated,
                WorksetsCreated = worksetsCreated,
                Failed = failed
            });
        }

        private static MultiSlopeVariantResult ProcessRow(
            UIApplication app, Document doc, Floor sourceFloor, MultiSlopePayload data, SlopeRowSetting row,
            Action<double> onRowProgress)
        {
            var result = new MultiSlopeVariantResult
            {
                RowIndex = row.RowIndex,
                Percent = row.Percent
            };

            Floor floorCopy = null;
            AutoSlopePayload slopePayload = null;
            GeometryApplyOutcome geo = null;
            string worksetName = null;

            using (Transaction tx = new Transaction(doc, $"AutoSlope Variant {row.Percent}% — Create & Slope"))
            {
                tx.Start();

                try
                {
                    // ── Copy the floor — zero translation keeps footprint and
                    // drain point XY identical to the source; only slope/Z changes.
                    ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElement(
                        doc, sourceFloor.Id, XYZ.Zero);

                    ElementId copyId = copiedIds.FirstOrDefault();
                    if (copyId == null || copyId == ElementId.InvalidElementId)
                        throw new InvalidOperationException("CopyElement returned no new element.");

                    floorCopy = doc.GetElement(copyId) as Floor;
                    if (floorCopy == null)
                        throw new InvalidOperationException("Copied element is not a floor.");

                    result.CopyFloorId = copyId;
                    data.Log?.Invoke(new LogEntry(LogLevel.Info,
                        $"Slope {row.Percent}%: copied floor {sourceFloor.Id} → {copyId}."));

                    // ── Apply this row's slope geometry to the copy ─────────
                    slopePayload = new AutoSlopePayload
                    {
                        FloorId = copyId,
                        PickedDrainPoints = data.PickedDrainPoints,
                        DrainPoints = data.DrainPoints,
                        SlopePercent = row.Percent,
                        ThresholdMeters = data.ThresholdMeters,
                        EnableDrainTolerance = data.EnableDrainTolerance,
                        DrainToleranceMm = data.DrainToleranceMm,
                        InsertCurveIntersectionPoints = data.InsertCurveIntersectionPoints,
                        ExportConfig = data.ExportConfig,
                        ProjectTitle = data.ProjectTitle,
                        DrainMarkerGroup = data.DrainMarkerGroup,
                        HighestPointMarkerGroup = data.HighestPointMarkerGroup,
                        AllowedOffsetMarkerGroup = data.AllowedOffsetMarkerGroup,
                        AllowedOffsetThresholdMm = data.AllowedOffsetThresholdMm,
                        Log = data.Log,
                        CancelToken = data.CancelToken,
                        Progress = info =>
                        {
                            if (info.PercentWithinPhase.HasValue)
                                onRowProgress?.Invoke(info.PercentWithinPhase.Value);
                            data.Log?.Invoke(new LogEntry(LogLevel.Info, $"[{row.Percent}%] {info.PhaseLabel}"));
                            ToolCancellation.CheckCurrent(); // pumps the window and asks "Cancel the process?"
                        }
                    };

                    geo = AutoSlopeEngine.ApplySlope(app, slopePayload);

                    if (!geo.Success)
                        throw new InvalidOperationException(geo.ErrorMessage ?? "Slope apply failed.");

                    // ── Resolve/create workset and assign the copy to it ────
                    worksetName = string.Format(data.WorksetNameFormat, FormatPercent(row.Percent));
                    WorksetResolver.Result ws = WorksetResolver.Resolve(doc, worksetName);

                    Parameter partitionParam = floorCopy.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (partitionParam == null || partitionParam.IsReadOnly)
                        throw new InvalidOperationException("Could not set workset on floor copy (ELEM_PARTITION_PARAM unavailable).");

                    partitionParam.Set(ws.WorksetId.IntegerValue);

                    result.WorksetName = worksetName;
                    result.WorksetWasCreated = ws.WasCreated;

                    tx.Commit();
                }
                catch (OperationCanceledException)
                {
                    tx.RollBack();

                    result.Success = false;
                    result.WasCancelled = true;
                    result.ErrorMessage = "Cancelled by user.";

                    data.Log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"Slope {row.Percent}% (row {row.RowIndex}) cancelled — rolled back, nothing written."));

                    return result;
                }
                catch (Exception ex)
                {
                    tx.RollBack();

                    result.Success = false;
                    result.ErrorMessage = ex.Message;

                    data.Log?.Invoke(new LogEntry(LogLevel.Error,
                        $"Slope {row.Percent}% (row {row.RowIndex}) failed: {ex.Message}"));

                    return result;
                }
            }

            // ── Geometry Transaction has committed — nothing is open on the
            // document now, so AutoSlopeParameterWriter can safely open its own
            // Transaction inside FinalizeAndExport. The floor copy and its
            // workset assignment are already committed at this point regardless
            // of what happens below, so a failure here is reported against this
            // row only — it does not roll back the geometry. ──────────────────
            try
            {
                AutoSlopeResult engineResult = AutoSlopeEngine.FinalizeAndExport(doc, floorCopy, slopePayload, geo);
                result.EngineResult = engineResult;
                result.Success = true;

                data.Log?.Invoke(new LogEntry(LogLevel.Success,
                    $"Slope {row.Percent}%: workset '{worksetName}' " +
                    $"({(result.WorksetWasCreated ? "newly created" : "existing")}) — floor {result.CopyFloorId} committed."));
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Floor copy and workset committed, but writing parameters/export failed: {ex.Message}";

                data.Log?.Invoke(new LogEntry(LogLevel.Error,
                    $"Slope {row.Percent}% (row {row.RowIndex}): floor {result.CopyFloorId} committed, " +
                    $"but writing parameters/export failed: {ex.Message}"));
            }

            return result;
        }

        private static void Abort(MultiSlopePayload data, string reason)
        {
            data.OnCompleted?.Invoke(new MultiSlopeRunSummary
            {
                Success = false,
                ErrorMessage = reason
            });
        }

        /// <summary>Trims a trailing ".0" so 1.0% logs/names as "1", while 1.5% stays "1.5".</summary>
        private static string FormatPercent(double percent) =>
            percent % 1 == 0 ? percent.ToString("0") : percent.ToString("0.##");
    }
}
