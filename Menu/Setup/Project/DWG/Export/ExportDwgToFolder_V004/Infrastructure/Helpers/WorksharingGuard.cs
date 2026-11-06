using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Infrastructure.Helpers
{
    /// <summary>
    /// Worksharing checks for the post-export delete / convert / re-link actions, which change or
    /// remove CAD instances in the model. NEW in 4.2.0.
    /// </summary>
    public static class WorksharingGuard
    {
        /// <summary>
        /// Tries to check out <paramref name="ids"/> in one call (no-op in a non-workshared
        /// model). Never throws; per-element results are read afterwards with
        /// <see cref="CanEdit"/>.
        /// </summary>
        /// <param name="doc">The document.</param>
        /// <param name="ids">Elements about to be changed.</param>
        public static void TryCheckout(Document doc, ICollection<ElementId> ids)
        {
            try
            {
                if (doc == null || !doc.IsWorkshared || ids == null || ids.Count == 0) return;
                WorksharingUtils.CheckoutElements(doc, ids);
            }
            catch
            {
                // CanEdit reports the real per-element state, so a failed bulk checkout is not fatal.
            }
        }

        /// <summary>
        /// True when the element can be edited by this user. In a workshared model it is false when
        /// another user owns the element, or when the central model has a newer or deleted version
        /// (a reload-latest is needed first).
        /// </summary>
        /// <param name="doc">The document.</param>
        /// <param name="id">The element to check.</param>
        /// <param name="reason">Why not, in one line; empty when editable.</param>
        public static bool CanEdit(Document doc, ElementId id, out string reason)
        {
            reason = string.Empty;
            try
            {
                if (doc == null || id == null || !doc.IsWorkshared) return true;

                if (WorksharingUtils.GetCheckoutStatus(doc, id, out string owner) == CheckoutStatus.OwnedByOtherUser)
                {
                    reason = string.IsNullOrWhiteSpace(owner)
                        ? "Checked out by another user."
                        : $"Checked out by {owner}.";
                    return false;
                }

                var update = WorksharingUtils.GetModelUpdatesStatus(doc, id);
                if (update == ModelUpdatesStatus.UpdatedInCentral || update == ModelUpdatesStatus.DeletedInCentral)
                {
                    reason = "Changed in the central model — Reload Latest first.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = $"Worksharing status could not be read ({ex.Message}).";
                return false;
            }
        }
    }
}
