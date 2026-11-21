using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>Turns Revit warnings off for a roof transaction and rolls it back on an error.</summary>
    public sealed class RoofFailuresPreprocessor : IFailuresPreprocessor
    {
        /// <summary>First error Revit raised, or null.</summary>
        public string Error { get; private set; }

        /// <inheritdoc/>
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(failure);
                    continue;
                }
                Error ??= failure.GetDescriptionText();
            }
            return Error == null ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }

    /// <summary>Creates one waterproofing roof from a planned job.</summary>
    public static class RoofCreator
    {
        /// <summary>
        /// True when a footprint roof can be made from <paramref name="roofType"/>. Only roof types with a
        /// compound structure qualify: sloped glazing (curtain) roof types have none, and
        /// <c>NewFootPrintRoof</c> refuses them. In-place roofs have no <see cref="RoofType"/> at all, so
        /// collecting that class already leaves them out.
        /// </summary>
        /// <param name="roofType">Roof type to test; null returns false.</param>
        public static bool IsFootPrintType(RoofType roofType)
        {
            if (roofType == null) return false;
            try
            {
                return roofType.GetCompoundStructure() != null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false; // a type Revit will not describe is not one to build a roof from
            }
        }

        /// <summary>The project's roof types a footprint roof can be made from.</summary>
        /// <param name="doc">Host document.</param>
        public static IReadOnlyList<RoofType> FootPrintTypes(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>()
                .Where(IsFootPrintType).ToList();

        /// <summary>Names of <see cref="FootPrintTypes"/>, sorted, for the roof-type list in the window.</summary>
        /// <param name="doc">Host document.</param>
        public static IReadOnlyList<string> FootPrintTypeNames(Document doc)
            => FootPrintTypes(doc).Select(t => t.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCulture).ToList();

        /// <summary>
        /// Creates the roof for <paramref name="job"/> in its own transaction, after deleting the
        /// roofs it replaces. Returns the new roof's id and area. Throws with a readable message
        /// when Revit refuses; the transaction is rolled back so nothing partial is left.
        /// </summary>
        /// <param name="doc">Host document.</param>
        /// <param name="job">What to create.</param>
        /// <param name="roofType">Roof type to use.</param>
        /// <param name="worksetId">Workset for the roof; null = leave it on the active workset.</param>
        public static (long RoofId, double AreaM2) Create(Document doc, RoofJob job, RoofType roofType, WorksetId worksetId)
        {
            if (doc.GetElement(new ElementId(job.HostLevelId)) is not Level level)
                throw new InvalidOperationException("The level for the roof no longer exists.");

            using var tx = new Transaction(doc, "WP roof " + job.Number);
            var failures = new RoofFailuresPreprocessor();
            FailureHandlingOptions options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(failures);
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            tx.Start();
            try
            {
                foreach (long oldId in job.ReplaceRoofIds)
                {
                    var id = new ElementId(oldId);
                    if (doc.GetElement(id) != null) doc.Delete(id);
                }

                double z = Elevations.InternalZFt(level);
                double shortest = doc.Application.ShortCurveTolerance;
                var footprint = new CurveArray();
                AddLoop(footprint, job.Outer, z, shortest);
                foreach (IReadOnlyList<Pt> hole in job.Inner) AddLoop(footprint, hole, z, shortest);

                FootPrintRoof roof = doc.Create.NewFootPrintRoof(footprint, level, roofType, out _);
                roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM)?.Set(job.BaseOffsetFromLevelMm / BoundaryReader.FtToMm);
                // Always written: re-runs and "Remove a run" find the roof by this tag.
                roof.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(job.CommentsTag);
                if (worksetId != null)
                {
                    Parameter workset = roof.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (workset != null && !workset.IsReadOnly) workset.Set(worksetId.IntegerValue);
                }

                doc.Regenerate();
                double areaM2 = (roof.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0) * BoundaryReader.Ft2ToM2;
                long roofId = roof.Id.Value;

                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException(failures.Error ?? "Revit did not accept the roof.");
                return (roofId, areaM2);
            }
            catch
            {
                if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                throw;
            }
        }

        /// <summary>Adds a closed outline as lines at height <paramref name="z"/>, leaving out points too close to make a valid line.</summary>
        private static void AddLoop(CurveArray footprint, IReadOnlyList<Pt> points, double z, double shortest)
        {
            var xyz = new List<XYZ>();
            foreach (Pt p in points)
            {
                var q = new XYZ(p.X / BoundaryReader.FtToMm, p.Y / BoundaryReader.FtToMm, z);
                if (xyz.Count == 0 || xyz[^1].DistanceTo(q) > shortest * 1.01) xyz.Add(q);
            }
            // Drop every trailing point that sits on the first one, so the closing line is never too short
            // for Revit. Stopping at three points would leave a degenerate closing line on a three-point loop.
            while (xyz.Count > 1 && xyz[^1].DistanceTo(xyz[0]) <= shortest * 1.01) xyz.RemoveAt(xyz.Count - 1);
            if (xyz.Count < 3) throw new InvalidOperationException("The outline is too small to make a roof.");
            for (int i = 0; i < xyz.Count; i++)
                footprint.Append(Line.CreateBound(xyz[i], xyz[(i + 1) % xyz.Count]));
        }
    }

    /// <summary>An earlier run found in the model.</summary>
    /// <param name="RunId">Run ID from the Comments tag.</param>
    /// <param name="Count">How many elements carry it.</param>
    public sealed record RunInfo(string RunId, int Count);

    /// <summary>Finds and removes what earlier runs created, by the run ID in Comments.</summary>
    public static class RunRemover
    {
        /// <summary>Runs that still have elements in the model, newest first.</summary>
        public static IReadOnlyList<RunInfo> List(Document doc)
            => Tagged(doc).GroupBy(t => t.RunId)
                .Select(g => new RunInfo(g.Key, g.Count()))
                .OrderByDescending(r => r.RunId, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Deletes the roofs, plans and section markers of run <paramref name="runId"/> in one transaction.
        /// Elements checked out by another user, or that Revit will not delete (the active view), are left
        /// and counted. Drafting views are never deleted.
        /// </summary>
        public static (int Deleted, int Left) Remove(Document doc, string runId)
        {
            var ids = Tagged(doc).Where(t => t.RunId == runId).Select(t => t.Id).Distinct().ToList();
            var free = ids.Where(id => RoomCollector.OwnerOf(doc, id) == null).ToList();
            if (free.Count == 0) return (0, ids.Count);
            int deleted = 0;

            using var tx = new Transaction(doc, "Remove waterproofing run " + runId);
            tx.Start();
            try
            {
                // Deleting a plan also deletes the markers on it, so each id is checked before it is deleted.
                foreach (ElementId id in free)
                {
                    if (doc.GetElement(id) == null) { deleted++; continue; } // went with its plan
                    try
                    {
                        doc.Delete(id);
                        deleted++;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // Revit refuses some deletes (a plan that is the active view, for one): it is left and counted.
                    }
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit did not accept the removal.");
            }
            catch
            {
                if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                throw;
            }
            return (deleted, ids.Count - deleted);
        }

        private static IEnumerable<(ElementId Id, string RunId)> Tagged(Document doc)
        {
            foreach (Element element in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Roofs).WhereElementIsNotElementType())
            {
                string comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                if (RunTag.TryParse(comments, out string runId, out _)) yield return (element.Id, runId);
            }

            // Plans and section markers carry the tag in Extensible Storage. Drafting views are left
            // alone on purpose: they may hold details the user has drawn.
            foreach (Element element in ElementTag.Tagged(doc))
                if (ElementTag.TryGet(element, out string runId, out _, out string kind)
                    && (kind == ElementTag.Plan || kind == ElementTag.Section) && !string.IsNullOrEmpty(runId))
                    yield return (element.Id, runId);
        }
    }
}
