using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Models;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Services
{
    /// <summary>Reads footprint roofs and their shape-editing points from the host model.</summary>
    public static class HostRoofService
    {
        /// <summary>
        /// Keeps only the footprint roofs among <paramref name="ids"/>.
        /// <paramref name="ignored"/> counts everything else (extrusion roofs, other categories, deleted ids).
        /// </summary>
        public static List<FootPrintRoof> FilterFootprintRoofs(Document doc, IEnumerable<ElementId> ids, out int ignored)
        {
            var roofs = new List<FootPrintRoof>();
            ignored = 0;
            foreach (ElementId id in ids)
            {
                if (doc.GetElement(id) is FootPrintRoof roof) roofs.Add(roof);
                else ignored++;
            }
            return roofs;
        }

        /// <summary>True for plan views (floor, structural and ceiling plans) that are not view templates.</summary>
        public static bool IsPlanView(View view) =>
            view is ViewPlan plan && !plan.IsTemplate &&
            (plan.ViewType == ViewType.FloorPlan || plan.ViewType == ViewType.EngineeringPlan || plan.ViewType == ViewType.CeilingPlan);

        /// <summary>Label shown in the grid: "Type name [id]".</summary>
        public static string Label(Element roof) => $"{roof.Name} [{roof.Id.Value}]";

        /// <summary>
        /// Reads the shape-editing points of <paramref name="roof"/>. Returns an empty list when shape
        /// editing is off (no points exist yet); <paramref name="reason"/> then says why.
        /// </summary>
        public static List<HostPoint> ReadPoints(FootPrintRoof roof, out string reason)
        {
            reason = null;
            SlabShapeEditor editor = roof.GetSlabShapeEditor();
            if (editor == null)
            {
                reason = "this roof type does not support shape editing";
                return new List<HostPoint>();
            }
            if (!editor.IsEnabled)
            {
                reason = "shape editing is not on (the roof has no shape-editing points)";
                return new List<HostPoint>();
            }

            string label = Label(roof);
            var points = new List<HostPoint>();
            int index = 0;
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
            {
                if (vertex?.Position == null) continue;
                points.Add(new HostPoint { RoofId = roof.Id, RoofLabel = label, Index = ++index, Position = vertex.Position });
            }
            if (points.Count == 0) reason = "the roof has no shape-editing points";
            return points;
        }

        /// <summary>
        /// Checks that <paramref name="roof"/> can be edited: not deleted and not owned by another user.
        /// Returns null when it can, otherwise the reason.
        /// </summary>
        public static string EditBlocker(Document doc, Element roof)
        {
            if (roof == null || !roof.IsValidObject) return "the roof no longer exists";
            if (doc.IsWorkshared)
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, roof.Id);
                if (status == CheckoutStatus.OwnedByOtherUser)
                    return $"the roof is checked out by {WorksharingUtils.GetWorksharingTooltipInfo(doc, roof.Id).Owner}";
            }
            return null;
        }
    }
}
