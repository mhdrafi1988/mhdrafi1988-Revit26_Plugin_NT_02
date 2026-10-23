using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services
{
    /// <summary>
    /// Decides whether this user can change an element: groups, worksharing ownership (element and
    /// workset) and elements changed in central since the last reload. Call only in the Revit API context.
    /// </summary>
    public sealed class EditabilityService
    {
        private readonly Document _doc;
        private readonly bool _workshared;
        private readonly string _me;
        private readonly Dictionary<WorksetId, Workset> _worksets = new();

        /// <summary>Creates the service for <paramref name="doc"/>.</summary>
        public EditabilityService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _workshared = doc.IsWorkshared;
            _me = doc.Application?.Username ?? string.Empty;
        }

        /// <summary>True when <paramref name="element"/> is a member of a group.</summary>
        public static bool InGroup(Element element)
            => element.GroupId != null && element.GroupId != ElementId.InvalidElementId;

        /// <summary>Workset name of <paramref name="element"/>, or empty in a non-workshared model.</summary>
        public string WorksetName(Element element) => _workshared ? GetWorkset(element.WorksetId)?.Name ?? string.Empty : string.Empty;

        /// <summary>
        /// Name of the other user who owns <paramref name="element"/> or its workset, or null when this
        /// user may edit it.
        /// </summary>
        public string OwnedByOther(Element element) => element == null ? null : OwnedByOther(element.Id, element.WorksetId);

        /// <summary>
        /// Why this user cannot change <paramref name="element"/> now, or null. Covers ownership by
        /// another user and elements updated or deleted in central (reload latest first).
        /// </summary>
        public string BlockReason(Element element)
        {
            if (element == null || !element.IsValidObject) return "it no longer exists";

            string owner = OwnedByOther(element);
            if (owner != null) return $"owned by {owner}";

            if (!_workshared) return null;
            try
            {
                var status = WorksharingUtils.GetModelUpdatesStatus(_doc, element.Id);
                if (status == ModelUpdatesStatus.UpdatedInCentral) return "changed in central; reload latest first";
                if (status == ModelUpdatesStatus.DeletedInCentral) return "deleted in central; reload latest first";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Status unknown (detached copy etc.): let Revit decide when the change is made.
            }
            return null;
        }

        private string OwnedByOther(ElementId id, WorksetId worksetId)
        {
            if (!_workshared) return null;

            try
            {
                if (WorksharingUtils.GetCheckoutStatus(_doc, id) == CheckoutStatus.OwnedByOtherUser)
                    return OwnerName(id);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }

            var ws = GetWorkset(worksetId);
            if (ws != null && !string.IsNullOrEmpty(ws.Owner) && !string.Equals(ws.Owner, _me, StringComparison.OrdinalIgnoreCase))
                return ws.Owner;
            return null;
        }

        private string OwnerName(ElementId id)
        {
            try
            {
                string owner = WorksharingUtils.GetWorksharingTooltipInfo(_doc, id).Owner;
                return string.IsNullOrEmpty(owner) ? "another user" : owner;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return "another user";
            }
        }

        private Workset GetWorkset(WorksetId id)
        {
            if (id == null || id == WorksetId.InvalidWorksetId) return null;
            if (_worksets.TryGetValue(id, out var ws)) return ws;
            try { ws = _doc.GetWorksetTable().GetWorkset(id); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { ws = null; }
            _worksets[id] = ws;
            return ws;
        }
    }
}
