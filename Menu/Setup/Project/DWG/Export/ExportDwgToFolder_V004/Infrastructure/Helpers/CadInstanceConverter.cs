using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Infrastructure.Helpers
{
    /// <summary>
    /// Swaps one CAD instance for another of the opposite kind, in place. NEW in V004.
    /// Two directions, both offered per row in the post-export review:
    /// <list type="bullet">
    /// <item><see cref="ConvertLinkToImport"/> — a CAD <i>link</i> becomes an <i>import</i> of the
    /// same source file.</item>
    /// <item><see cref="ReplaceWithLinkToExportedFile"/> — any instance becomes a <i>link</i> to
    /// the DWG this tool just exported, which is the move that shrinks the model and leaves the CAD
    /// editable outside Revit.</item>
    /// </list>
    /// <para>
    /// Revit exposes no API for Manage Links' "Import" button, so neither direction can be done in
    /// place. The only route — and the one the Revit API forum recommends — is: read the instance's
    /// transform, delete it, create the replacement, then move the replacement back onto the old
    /// one's position. That is what both methods do, sharing
    /// <see cref="RestorePlacement"/> for the last part.
    /// </para>
    /// <para>
    /// Linking back to the exported file is the easier of the two: that DWG was written from this
    /// very instance, so it already carries the right position and lands correctly with no rotation
    /// to work out. The link→import direction has to re-derive the placement, which is why it
    /// reports what it did.
    /// </para>
    /// <para>
    /// What neither direction can carry over, and therefore reports instead of losing quietly:
    /// per-view graphic overrides and layer visibility/graphic overrides set on the old instance;
    /// anything dimensioned, aligned or otherwise constrained to its geometry (deleting it may post
    /// a Revit error or throw, which fails that one row); and the element id, which changes — any
    /// schedule, filter or other tool keyed to the old id needs updating.
    /// </para>
    /// <para>
    /// The caller runs each conversion inside its own <see cref="Transaction"/>, so one that fails
    /// half-way rolls back on its own and leaves the original instance where it was.
    /// </para>
    /// </summary>
    public static class CadInstanceConverter
    {
        /// <summary>Below this the rotation is treated as zero (radians).</summary>
        private const double AngleTolerance = 1e-9;

        /// <summary>What has to survive the delete.</summary>
        private class InstanceState
        {
            public Transform Transform;
            public bool WasViewSpecific;
            public ElementId OwnerViewId = ElementId.InvalidElementId;
            public bool WasPinned;
            public ElementId TypeId = ElementId.InvalidElementId;
            public int? WorksetIntegerId;
        }

        // ── Link → Import ───────────────────────────────────────────────────

        /// <summary>
        /// Replaces a CAD link with an import of the same source file, inside the transaction the
        /// caller has already opened.
        /// </summary>
        /// <param name="doc">The document. Must be in an open transaction.</param>
        /// <param name="decision">
        /// The decision row; its result fields (<c>Succeeded</c>, <c>Note</c>,
        /// <c>NewElementIdValue</c>) are filled in.
        /// </param>
        /// <param name="fallbackViewId">View to create the import in when the instance is not view-specific.</param>
        /// <returns>True when the import was created and re-placed.</returns>
        public static bool ConvertLinkToImport(Document doc, PostExportDecision decision, ElementId fallbackViewId)
        {
            if (doc == null || decision == null) return false;
            decision.Succeeded = false;

            if (decision.SourceType != DwgSourceType.Link)
            {
                decision.Note = "Not a link — an import has no external file to re-import.";
                return false;
            }

            string path = decision.LinkedSourcePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                decision.Note = "Source path unresolved — nothing to re-import.";
                return false;
            }

            if (!FileReadable(path, out string pathProblem))
            {
                decision.Note = pathProblem;
                return false;
            }

            return Replace(doc, decision, fallbackViewId, path, asLink: false);
        }

        // ── Anything → Link to the exported file ────────────────────────────

        /// <summary>
        /// Replaces the instance with a CAD link to the DWG this tool exported for it, inside the
        /// transaction the caller has already opened. Works for an import (the model shrinks and the
        /// CAD becomes editable outside Revit) and for a link (it is re-pointed at the exported copy).
        /// </summary>
        /// <param name="doc">The document. Must be in an open transaction.</param>
        /// <param name="decision">The decision row; its result fields are filled in.</param>
        /// <param name="fallbackViewId">View to create the link in when the instance is not view-specific.</param>
        /// <returns>True when the link was created and re-placed.</returns>
        public static bool ReplaceWithLinkToExportedFile(Document doc, PostExportDecision decision, ElementId fallbackViewId)
        {
            if (doc == null || decision == null) return false;
            decision.Succeeded = false;

            string path = decision.ExportedFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                decision.Note = "No exported file recorded for this row — nothing to link to.";
                return false;
            }

            if (!FileReadable(path, out string pathProblem))
            {
                decision.Note = pathProblem;
                return false;
            }

            // Linking to a file inside the model's own temp or a volatile folder would be a trap, so
            // the note says plainly what the link now depends on.
            return Replace(doc, decision, fallbackViewId, path, asLink: true);
        }

        // ── Shared body ─────────────────────────────────────────────────────

        /// <summary>
        /// Deletes the instance and recreates it from <paramref name="path"/>, as a link when
        /// <paramref name="asLink"/> is true and an import otherwise, then puts it back where the
        /// original was.
        /// </summary>
        private static bool Replace(Document doc, PostExportDecision decision, ElementId fallbackViewId,
                                    string path, bool asLink)
        {
            var inst = doc.GetElement(decision.ElementId) as ImportInstance;
            if (inst == null)
            {
                decision.Note = "The CAD instance is no longer in the model.";
                return false;
            }

            var state = Capture(inst);

            // Which view the replacement lands in. A view-specific instance goes back into its own
            // view; a model-space one goes into whichever view the scan found it in, since both
            // Document.Import and Document.Link always need a view.
            ElementId targetViewId = state.WasViewSpecific && state.OwnerViewId != ElementId.InvalidElementId
                ? state.OwnerViewId
                : (decision.OwnerViewId != null && decision.OwnerViewId != ElementId.InvalidElementId
                    ? decision.OwnerViewId
                    : fallbackViewId);

            if (!(doc.GetElement(targetViewId) is View targetView))
            {
                decision.Note = "No view available to place the replacement in — open a plan view and try again.";
                return false;
            }

            // ── Delete the original ──────────────────────────────────────────
            try
            {
                doc.Delete(decision.ElementId);
            }
            catch (Exception ex)
            {
                // Most often: something is dimensioned or aligned to the CAD geometry.
                decision.Note =
                    "Could not delete the original — something in the model may be dimensioned or " +
                    $"aligned to it. ({ex.Message})";
                return false;
            }

            // Drop a now-unreferenced CADLinkType so the file stops appearing in Manage Links.
            TryDeleteOrphanedLinkType(doc, state.TypeId);

            // ── Create the replacement ───────────────────────────────────────
            ElementId newId;
            string whatFailed = asLink ? "link" : "import";
            try
            {
                var opts = new DWGImportOptions
                {
                    // Keep the file's own colours rather than inverting or flattening them — the
                    // whole point of this tool is fidelity to the CAD.
                    ColorMode = ImportColorMode.Preserved,
                    Placement = ImportPlacement.Origin,
                    ThisViewOnly = state.WasViewSpecific,
                    Unit = ImportUnit.Default,
                    OrientToView = false,
                    AutoCorrectAlmostVHLines = false,
                    VisibleLayersOnly = false
                };

                bool created = asLink
                    ? doc.Link(path, opts, targetView, out newId)
                    : doc.Import(path, opts, targetView, out newId);

                if (!created || newId == null || newId == ElementId.InvalidElementId)
                {
                    decision.Note =
                        $"Revit refused the {whatFailed} — the original has been left deleted; undo (Ctrl+Z) to restore it.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                decision.Note = $"Creating the {whatFailed} failed ({ex.Message}) — undo (Ctrl+Z) to restore the original.";
                return false;
            }

            // ── Put it back where the original was ───────────────────────────
            var notes = new List<string>();
            RestorePlacement(doc, newId, state, notes);
            RestoreWorksetAndPin(doc, newId, state, notes);

            // ── What never carries over ──────────────────────────────────────
            notes.Add("per-view graphic and layer overrides from the original do not carry over");
            notes.Add($"element ID changed {decision.ElementIdValue} → {newId.Value}");
            notes.Add(state.WasViewSpecific ? "kept as this-view-only" : "placed in all views");
            if (asLink)
                notes.Add($"now linked to {path} — keep that file where it is");

            decision.NewElementIdValue = newId.Value;
            decision.Succeeded = true;
            decision.Note = (asLink ? "Replaced with a link to the exported file — " : "Converted to import — ")
                            + string.Join("; ", notes) + ".";
            return true;
        }

        /// <summary>Reads everything off the instance that has to survive its deletion.</summary>
        private static InstanceState Capture(ImportInstance inst)
        {
            var state = new InstanceState
            {
                WasViewSpecific = inst.ViewSpecific,
                OwnerViewId = inst.OwnerViewId ?? ElementId.InvalidElementId,
                WasPinned = inst.Pinned,
                TypeId = inst.GetTypeId()
            };

            try { state.Transform = inst.GetTotalTransform(); }
            catch { /* re-placement is skipped and reported */ }

            try
            {
                var worksetParam = inst.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (worksetParam != null && !worksetParam.IsReadOnly)
                    state.WorksetIntegerId = worksetParam.AsInteger();
            }
            catch { /* not workshared, or not writable — nothing to restore */ }

            return state;
        }

        /// <summary>
        /// Rotates then moves the new instance so it sits exactly where the old one did. Rotation
        /// first, about the new instance's own origin, so it doesn't move the point being lined up.
        /// </summary>
        private static void RestorePlacement(Document doc, ElementId newId, InstanceState state, List<string> notes)
        {
            if (state.Transform == null)
            {
                notes.Add("the original's placement could not be read, so the replacement sits at the import origin");
                return;
            }

            Transform newTransform = null;
            try { newTransform = (doc.GetElement(newId) as ImportInstance)?.GetTotalTransform(); }
            catch { /* reported below */ }

            if (newTransform == null)
            {
                notes.Add("the replacement's placement could not be read, so it was not moved");
                return;
            }

            try
            {
                double angle = PlanAngleBetween(newTransform.BasisX, state.Transform.BasisX);
                if (Math.Abs(angle) > AngleTolerance)
                {
                    using (var axis = Line.CreateUnbound(newTransform.Origin, XYZ.BasisZ))
                    {
                        ElementTransformUtils.RotateElement(doc, newId, axis, angle);
                    }
                }

                var afterRotate = (doc.GetElement(newId) as ImportInstance)?.GetTotalTransform();
                var from = afterRotate?.Origin ?? newTransform.Origin;
                var delta = state.Transform.Origin - from;
                if (!delta.IsZeroLength())
                    ElementTransformUtils.MoveElement(doc, newId, delta);
            }
            catch (Exception ex)
            {
                notes.Add($"could not be moved onto the original's position ({ex.Message})");
            }
        }

        /// <summary>Puts the replacement on the same workset and re-pins it, where it can.</summary>
        private static void RestoreWorksetAndPin(Document doc, ElementId newId, InstanceState state, List<string> notes)
        {
            if (state.WorksetIntegerId.HasValue)
            {
                try
                {
                    var worksetParam = doc.GetElement(newId)?.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (worksetParam != null && !worksetParam.IsReadOnly)
                        worksetParam.Set(state.WorksetIntegerId.Value);
                }
                catch { notes.Add("workset could not be restored"); }
            }

            if (!state.WasPinned) return;

            try
            {
                var newInst = doc.GetElement(newId);
                if (newInst != null) newInst.Pinned = true;
            }
            catch { notes.Add("pin could not be restored"); }
        }

        /// <summary>Checks a path is there and readable, with a reason when it isn't.</summary>
        private static bool FileReadable(string path, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(path))
                {
                    problem = $"File not found: {path}";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                problem = $"File could not be read ({ex.Message}): {path}";
                return false;
            }
        }

        /// <summary>
        /// Signed angle, about Z, that rotates <paramref name="from"/> onto <paramref name="to"/>
        /// in plan. Vectors with no horizontal component give 0.
        /// </summary>
        private static double PlanAngleBetween(XYZ from, XYZ to)
        {
            if (from == null || to == null) return 0;

            var a = new XYZ(from.X, from.Y, 0);
            var b = new XYZ(to.X, to.Y, 0);
            if (a.IsZeroLength() || b.IsZeroLength()) return 0;

            a = a.Normalize();
            b = b.Normalize();

            // atan2 of the cross product's Z against the dot product — signed, and stable near 180°
            // where acos(dot) loses its sign.
            double cross = a.X * b.Y - a.Y * b.X;
            double dot = a.X * b.X + a.Y * b.Y;
            return Math.Atan2(cross, dot);
        }

        /// <summary>
        /// Deletes <paramref name="typeId"/> when it is a <see cref="CADLinkType"/> with no
        /// remaining instances, so the file stops being listed in Manage Links. Silent on failure —
        /// a leftover type is cosmetic, not a reason to fail the conversion.
        /// </summary>
        private static void TryDeleteOrphanedLinkType(Document doc, ElementId typeId)
        {
            if (typeId == null || typeId == ElementId.InvalidElementId) return;

            try
            {
                if (!(doc.GetElement(typeId) is CADLinkType)) return;

                bool stillUsed = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .Any(i => i.GetTypeId() == typeId);

                if (!stillUsed)
                    doc.Delete(typeId);
            }
            catch { /* leave the type in place */ }
        }
    }
}
