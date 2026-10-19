using Autodesk.Revit.DB;

namespace Revit26_Plugin.BulkRename.V001.Core.Services
{
    /// <summary>Worksharing check shared by the item sources and the rename step.</summary>
    public static class WorksharingGuard
    {
        /// <summary>
        /// Returns why <paramref name="id"/> cannot be edited because another user owns it, or null when
        /// it can (including when the model is not workshared).
        /// </summary>
        public static string OwnerLock(Document doc, ElementId id)
        {
            if (doc == null || !doc.IsWorkshared) return null;

            try
            {
                if (WorksharingUtils.GetCheckoutStatus(doc, id) != CheckoutStatus.OwnedByOtherUser) return null;
                return $"Owned by {Owner(doc, id)}. Ask them to synchronize and relinquish.";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Status unavailable (for example, central model not reachable): do not block the item.
                return null;
            }
        }

        private static string Owner(Document doc, ElementId id)
        {
            try { return WorksharingUtils.GetWorksharingTooltipInfo(doc, id).Owner; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return "another user"; }
        }
    }
}
