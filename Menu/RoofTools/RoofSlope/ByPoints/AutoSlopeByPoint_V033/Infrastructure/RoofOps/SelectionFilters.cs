// =======================================================
// File: SelectionFilters.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// New in V032 (plan 1.2). Revit only lets the user click valid targets, so
// V030's "one or more drain points were not on the roof — re-run the
// command" dead end can no longer happen.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    /// <summary>Only roofs can be picked.</summary>
    public sealed class RoofSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is RoofBase;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    /// <summary>Only points on one specific roof can be picked.</summary>
    public sealed class PointOnRoofFilter : ISelectionFilter
    {
        private readonly ElementId _roofId;
        public PointOnRoofFilter(ElementId roofId) => _roofId = roofId;

        public bool AllowElement(Element elem) => elem != null && elem.Id == _roofId;
        public bool AllowReference(Reference reference, XYZ position) =>
            reference != null && reference.ElementId == _roofId;
    }
}
