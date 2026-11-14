// =======================================================
// File: SelectionFilters.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Infrastructure.Helpers
// Same filters as Floor Slope By Point V001: only host floors outside groups,
// then only points on the chosen floor, can be picked.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Infrastructure.Helpers
{
    /// <summary>Only host-model floors outside groups can be picked.</summary>
    public sealed class FloorSelectionFilter : ISelectionFilter
    {
        /// <summary>True for a floor that is not in a group.</summary>
        public bool AllowElement(Element elem) =>
            elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);

        /// <summary>Any reference on an allowed floor.</summary>
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    /// <summary>Only points on one specific floor can be picked.</summary>
    public sealed class PointOnFloorFilter : ISelectionFilter
    {
        private readonly ElementId _floorId;
        /// <summary>Restricts picks to the floor with <paramref name="floorId"/>.</summary>
        public PointOnFloorFilter(ElementId floorId) => _floorId = floorId;

        /// <summary>True only for the chosen floor.</summary>
        public bool AllowElement(Element elem) => elem != null && elem.Id == _floorId;

        /// <summary>True only for points on the chosen floor.</summary>
        public bool AllowReference(Reference reference, XYZ position) =>
            reference != null && reference.ElementId == _floorId;
    }
}
