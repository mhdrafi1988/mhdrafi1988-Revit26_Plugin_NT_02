// =======================================================
// File: SelectionFilters.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Infrastructure.FloorOps
// New in V032 (plan 1.2). Revit only lets the user click valid targets, so
// V030's "one or more drain points were not on the floor — re-run the
// command" dead end can no longer happen.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Infrastructure.FloorOps
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
