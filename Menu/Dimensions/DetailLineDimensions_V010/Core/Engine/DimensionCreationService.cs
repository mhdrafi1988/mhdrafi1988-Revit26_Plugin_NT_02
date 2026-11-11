using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Revit26_Plugin_Revised.DtlLineDim.V010.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DtlLineDim.V010.Core.Engine
{
    /// <summary>
    /// Creates linear length dimensions across line-based detail-item instances,
    /// using two perpendicular "tick" detail curves as dimension references.
    /// </summary>
    public static class DimensionCreationService
    {
        public class Result
        {
            public int Created;
            public int Failed;
            public TransactionStatus Status;
        }

        /// <summary>
        /// Suppresses Revit's interactive failure/warning dialogs (e.g. duplicate
        /// curve, zero-length dimension) during batch creation, downgrading them
        /// to entries deleted from the failure list so the transaction can proceed
        /// without blocking on a dialog.
        /// </summary>
        private class SuppressWarningsPreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                var failures = failuresAccessor.GetFailureMessages();
                foreach (var f in failures)
                {
                    if (f.GetSeverity() == FailureSeverity.Warning)
                        failuresAccessor.DeleteWarning(f);
                }
                return FailureProcessingResult.Continue;
            }
        }

        public static Result CreateDimensions(
            Document doc,
            View view,
            ComboItem detailType,
            ComboItem dimType,
            double tickSizeMm,
            double offsetMm,
            ObservableCollection<LogEntry> log)
        {
            var result = new Result();

            log.Add(new LogEntry(LogLevel.Info,
                $"GenerateDimensions started. DetailType='{detailType?.Name}', DimType='{dimType?.Name}', Tick={tickSizeMm}mm, Offset={offsetMm}mm"));

            DimensionType dimTypeElem = doc.GetElement(dimType.ElementId) as DimensionType;
            if (dimTypeElem == null)
            {
                log.Add(new LogEntry(LogLevel.Error, "Invalid Dimension Type — aborting."));
                result.Status = TransactionStatus.Uninitialized;
                return result;
            }

            var instances = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi => fi.Symbol.Id == detailType.ElementId)
                .ToList();

            log.Add(new LogEntry(LogLevel.Info, $"Matching detail items found: {instances.Count}"));

            if (instances.Count == 0)
            {
                log.Add(new LogEntry(LogLevel.Warning, "No matching line-based detail items found."));
                result.Status = TransactionStatus.Uninitialized;
                return result;
            }

            double tickSize = UnitUtils.ConvertToInternalUnits(tickSizeMm, UnitTypeId.Millimeters);
            double offset = UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters);

            using Transaction tx = new Transaction(doc, "Line-Based Detail Length Dimension");

            try
            {
                tx.Start();

                var failureOptions = tx.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(new SuppressWarningsPreprocessor());
                tx.SetFailureHandlingOptions(failureOptions);
            }
            catch (Exception ex)
            {
                log.Add(new LogEntry(LogLevel.Error, $"Failed to start transaction: {ex.Message}"));
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                result.Status = TransactionStatus.Uninitialized;
                return result;
            }

            foreach (var fi in instances)
            {
                ToolCancellation.CheckCurrent(); // outside the per-item try, so a cancel is never counted as a failed item
                SubTransaction subTx = null;

                try
                {
                    subTx = new SubTransaction(doc);
                    subTx.Start();

                    // Validate straight line-based detail item
                    if (fi.Location is not LocationCurve lc)
                        throw new Exception("Detail item has no LocationCurve.");

                    if (lc.Curve is not Line baseLine)
                        throw new Exception("Detail item is not a straight line.");

                    XYZ p0 = baseLine.GetEndPoint(0);
                    XYZ p1 = baseLine.GetEndPoint(1);

                    XYZ lineDir = baseLine.Direction.Normalize();

                    // Guard against near-parallel-to-Z lines before the cross product,
                    // rather than relying on Normalize() to throw after the fact.
                    if (lineDir.CrossProduct(XYZ.BasisZ).IsZeroLength())
                        throw new Exception("Detail line is parallel to view Z-axis — cannot compute perpendicular.");

                    XYZ perpDir = lineDir.CrossProduct(XYZ.BasisZ).Normalize();

                    // START tick
                    Line startTick = Line.CreateBound(
                        p0 - perpDir * tickSize,
                        p0 + perpDir * tickSize);
                    DetailCurve startRef = doc.Create.NewDetailCurve(view, startTick);

                    // END tick
                    Line endTick = Line.CreateBound(
                        p1 - perpDir * tickSize,
                        p1 + perpDir * tickSize);
                    DetailCurve endRef = doc.Create.NewDetailCurve(view, endTick);

                    ReferenceArray ra = new ReferenceArray();
                    ra.Append(startRef.GeometryCurve.Reference);
                    ra.Append(endRef.GeometryCurve.Reference);

                    // Dimension line, parallel to detail line, offset by configured amount
                    Line dimLine = Line.CreateBound(
                        p0 + perpDir * offset,
                        p1 + perpDir * offset);

                    Dimension newDim = doc.Create.NewDimension(view, dimLine, ra, dimTypeElem);

                    subTx.Commit();
                    result.Created++;

                    // Per-item traceability: log the source detail item and the
                    // resulting dimension element, not just the aggregate count.
                    log.Add(new LogEntry(LogLevel.Info,
                        $"Dimensioned detail item {fi.Id.Value} -> dimension {newDim.Id.Value}"));
                }
                catch (Exception ex)
                {
                    // Roll back only this item's tick curves — prevents orphan
                    // DetailCurves from a partially-completed item reaching the
                    // outer commit alongside successful items. Guarded because
                    // subTx may be null/unstarted if construction itself failed.
                    if (subTx != null && subTx.GetStatus() == TransactionStatus.Started)
                        subTx.RollBack();

                    result.Failed++;
                    log.Add(new LogEntry(LogLevel.Warning, $"Skipped {fi.Id.Value}: {ex.Message}"));
                }
                finally
                {
                    subTx?.Dispose();
                }
            }

            ToolCancellation.CheckCurrent(); // last chance to cancel before the commit (tx rolls back with the exception)
            if (result.Created > 0)
            {
                result.Status = tx.Commit();
                if (result.Status != TransactionStatus.Committed)
                    log.Add(new LogEntry(LogLevel.Error, $"Transaction did not commit cleanly. Status: {result.Status}"));
            }
            else
            {
                tx.RollBack();
                result.Status = TransactionStatus.RolledBack;
            }

            var summaryLevel = result.Failed > 0
                ? (result.Created > 0 ? LogLevel.Warning : LogLevel.Error)
                : LogLevel.Success;

            log.Add(new LogEntry(summaryLevel, $"Created: {result.Created}, Failed: {result.Failed}"));

            return result;
        }
    }
}
