using System;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers
{
    /// <summary>
    /// Decides whether an annotation may be moved: exists, not pinned, not in a group, not owned by
    /// another user (element or workset), not changed in central since the last reload.
    /// Call only in the Revit API context.
    /// </summary>
    internal sealed class EditabilityCheck
    {
        private readonly Document _doc;
        private readonly string _me;

        /// <summary>Creates the check for <paramref name="doc"/>.</summary>
        public EditabilityCheck(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _me = doc.Application?.Username ?? string.Empty;
        }

        /// <summary>Why <paramref name="element"/> cannot be moved, or null when it can.</summary>
        public string BlockReason(Element element)
        {
            if (element == null || !element.IsValidObject)
                return "no longer exists";
            if (element.Pinned)
                return "pinned";
            if (element.GroupId != null && element.GroupId != ElementId.InvalidElementId)
                return "in a group";
            if (!_doc.IsWorkshared)
                return null;

            try
            {
                if (WorksharingUtils.GetCheckoutStatus(_doc, element.Id, out string owner) == CheckoutStatus.OwnedByOtherUser)
                    return $"owned by {(string.IsNullOrEmpty(owner) ? "another user" : owner)}";

                Workset ws = _doc.GetWorksetTable().GetWorkset(element.WorksetId);
                if (ws != null && !string.IsNullOrEmpty(ws.Owner)
                    && !string.Equals(ws.Owner, _me, StringComparison.OrdinalIgnoreCase))
                    return $"workset owned by {ws.Owner}";

                ModelUpdatesStatus status = WorksharingUtils.GetModelUpdatesStatus(_doc, element.Id);
                if (status == ModelUpdatesStatus.UpdatedInCentral)
                    return "changed in central; reload latest";
                if (status == ModelUpdatesStatus.DeletedInCentral)
                    return "deleted in central; reload latest";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Status unknown (detached copy etc.): let Revit decide when the move is made.
            }

            return null;
        }
    }
}
