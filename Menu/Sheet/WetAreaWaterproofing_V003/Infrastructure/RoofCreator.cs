using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Run;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Infrastructure
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
        /// Why a footprint roof cannot be made from <paramref name="roofType"/>, in words for the user,
        /// or null when one can. Only roof types with layers qualify: sloped glazing (curtain) roof types
        /// have none, and <c>NewFootPrintRoof</c> then throws <c>ArgumentNullException</c> with no
        /// parameter name, whose message is the bare "Value cannot be null." In-place roofs have no
        /// <see cref="RoofType"/> at all, so collecting that class already leaves them out.
        /// </summary>
        /// <param name="roofType">Roof type to test; null gives a message.</param>
        public static string WhyNotUsable(RoofType roofType)
        {
            if (roofType == null || !roofType.IsValidObject)
                return "The roof type is no longer in the project. Press Refresh and pick another.";

            CompoundStructure structure;
            try
            {
                structure = roofType.GetCompoundStructure();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                structure = null; // a type Revit will not describe is not one to build a roof from
            }
            return structure == null || structure.LayerCount == 0
                ? $"Roof type \"{roofType.Name}\" has no layers, so Revit cannot make a footprint roof from it "
                  + "(sloped glazing types are like this). Pick a basic roof type."
                : null;
        }

        /// <summary>True when a footprint roof can be made from <paramref name="roofType"/>.</summary>
        /// <param name="roofType">Roof type to test; null returns false.</param>
        public static bool IsFootPrintType(RoofType roofType) => WhyNotUsable(roofType) == null;

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
        /// roofs it replaces. Returns the new roof's id, its area and how many of the room's holes
        /// Revit would not cut. Throws with a readable message when Revit refuses — one that names the
        /// exception and the values Revit was given — and the transaction is rolled back, so nothing
        /// partial is left.
        /// </summary>
        /// <param name="doc">Host document.</param>
        /// <param name="job">What to create.</param>
        /// <param name="roofType">Roof type to use.</param>
        /// <param name="worksetId">Workset for the roof; null = leave it on the active workset.</param>
        public static (long RoofId, double AreaM2, int HolesSkipped) Create(Document doc, RoofJob job, RoofType roofType, WorksetId worksetId)
        {
            if (doc == null || !doc.IsValidObject)
                throw new InvalidOperationException("The document was closed. Close this window and start the tool again.");
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (job.Outer == null || job.Outer.Count < 3)
                throw new InvalidOperationException("The room has no closed outline to make a roof from.");

            // Checked here as well as before the run: the type may have been deleted or changed since.
            string refused = WhyNotUsable(roofType);
            if (refused != null) throw new InvalidOperationException(refused);

            if (doc.GetElement(new ElementId(job.HostLevelId)) is not Level level || !level.IsValidObject)
                throw new InvalidOperationException("The level for the roof no longer exists.");

            using var tx = new Transaction(doc, "WP roof " + job.Number);
            var failures = new RoofFailuresPreprocessor();
            FailureHandlingOptions options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(failures);
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            tx.Start();

            List<XYZ> outer = null;
            var holes = new List<List<XYZ>>();
            try
            {
                foreach (long oldId in job.ReplaceRoofIds ?? Array.Empty<long>())
                {
                    var id = new ElementId(oldId);
                    if (doc.GetElement(id) != null) doc.Delete(id);
                }

                double z = Elevations.InternalZFt(level);
                double shortest = doc.Application.ShortCurveTolerance;
                int holesSkipped = 0;
                outer = Loop(job.Outer, z, shortest);
                foreach (IReadOnlyList<Pt> hole in job.Inner ?? Array.Empty<IReadOnlyList<Pt>>())
                {
                    try { holes.Add(Loop(hole, z, shortest)); }
                    catch (InvalidOperationException) { holesSkipped++; } // too small for Revit: the roof covers it
                }

                FootPrintRoof roof = NewRoof(doc, outer, holes, level, roofType, out int dropped);
                holesSkipped += dropped;

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
                return (roofId, areaM2, holesSkipped);
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                throw Explained(ex, level, roofType, outer, holes);
            }
        }

        /// <summary>
        /// One call to <c>NewFootPrintRoof</c> with the outer loop and the holes, and a second with the
        /// outer loop alone when Revit refuses that footprint: the method takes one flat
        /// <see cref="CurveArray"/> and has no documented support for inner loops, and a roof that covers
        /// a hole is worth more than no roof at all. The first attempt runs in a
        /// <see cref="SubTransaction"/>, so a refusal leaves nothing behind.
        /// </summary>
        /// <param name="holesSkipped">How many holes were dropped to get the roof made.</param>
        private static FootPrintRoof NewRoof(Document doc, List<XYZ> outer, List<List<XYZ>> holes,
            Level level, RoofType roofType, out int holesSkipped)
        {
            holesSkipped = 0;
            if (holes.Count > 0)
            {
                using var sub = new SubTransaction(doc);
                sub.Start();
                try
                {
                    FootPrintRoof withHoles = Make(doc, Footprint(outer, holes), level, roofType);
                    sub.Commit();
                    return withHoles;
                }
                catch (Exception ex) when (ex is not OperationCanceledException
                                           and not Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    sub.RollBack();
                    holesSkipped = holes.Count;
                }
            }
            return Make(doc, Footprint(outer, new List<List<XYZ>>()), level, roofType);
        }

        /// <summary>
        /// The only place <c>NewFootPrintRoof</c> is called. The mapping array is handed to Revit
        /// ready-made, the way the Revit SDK's own sample and this add-in's proven roof services do,
        /// never as an empty <c>out _</c> slot.
        /// </summary>
        private static FootPrintRoof Make(Document doc, CurveArray footprint, Level level, RoofType roofType)
        {
            ModelCurveArray mapping = new ModelCurveArray();
            FootPrintRoof roof = doc.Create.NewFootPrintRoof(footprint, level, roofType, out mapping);
            return roof ?? throw new InvalidOperationException("Revit made no roof from the outline.");
        }

        /// <summary>One flat <see cref="CurveArray"/>: the outer loop, then one closed loop per hole.</summary>
        private static CurveArray Footprint(List<XYZ> outer, List<List<XYZ>> holes)
        {
            var footprint = new CurveArray();
            Append(footprint, outer);
            foreach (List<XYZ> hole in holes) Append(footprint, hole);
            return footprint;
        }

        /// <summary>Appends one closed loop of points as bound lines.</summary>
        private static void Append(CurveArray footprint, List<XYZ> loop)
        {
            for (int i = 0; i < loop.Count; i++)
                footprint.Append(Line.CreateBound(loop[i], loop[(i + 1) % loop.Count]));
        }

        /// <summary>
        /// The exception to report for a roof Revit would not make. Our own messages are already clear
        /// and are passed on as they are; anything else — Revit's bare "Value cannot be null." above all —
        /// gets the exception's type and the values Revit was given added to it, so the report row and the
        /// log say which of them to look at.
        /// </summary>
        private static Exception Explained(Exception ex, Level level, RoofType roofType,
            List<XYZ> outer, List<List<XYZ>> holes)
        {
            if (ex is OperationCanceledException or Autodesk.Revit.Exceptions.OperationCanceledException) return ex;
            if (ex is InvalidOperationException && ex.InnerException == null) return ex;

            int layers = 0;
            try { layers = roofType?.GetCompoundStructure()?.LayerCount ?? 0; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* diagnostics only */ }

            return new InvalidOperationException(
                $"{ex.Message} [{ex.GetType().Name}; roof type \"{roofType?.Name}\", {layers} layer(s); "
                + $"level \"{level?.Name}\"; {outer?.Count ?? 0} outline point(s), {holes?.Count ?? 0} hole(s)]", ex);
        }

        /// <summary>
        /// The points of one closed outline at height <paramref name="z"/>, leaving out points too close
        /// together to make a valid line.
        /// </summary>
        private static List<XYZ> Loop(IReadOnlyList<Pt> points, double z, double shortest)
        {
            var xyz = new List<XYZ>();
            foreach (Pt p in points ?? Array.Empty<Pt>())
            {
                var q = new XYZ(p.X / BoundaryReader.FtToMm, p.Y / BoundaryReader.FtToMm, z);
                if (xyz.Count == 0 || xyz[^1].DistanceTo(q) > shortest * 1.01) xyz.Add(q);
            }
            // Drop every trailing point that sits on the first one, so the closing line is never too short
            // for Revit. Stopping at three points would leave a degenerate closing line on a three-point loop.
            while (xyz.Count > 1 && xyz[^1].DistanceTo(xyz[0]) <= shortest * 1.01) xyz.RemoveAt(xyz.Count - 1);
            if (xyz.Count < 3) throw new InvalidOperationException("The outline is too small to make a roof.");
            return xyz;
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
