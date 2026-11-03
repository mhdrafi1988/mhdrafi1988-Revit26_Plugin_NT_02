// =======================================================
// File: FloorPickFilter.cs
// Namespace: Revit26_Plugin_Revised.Shared.Services
// Selection filter for the floor tools (plugin 6.34.0): only host-model
// floors that are not in a group can be picked, so a pick never lands on an
// element FloorShapeEditChecks would refuse for that reason.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>Allows only floors outside groups (host model; linked elements never reach this filter).</summary>
    public sealed class FloorPickFilter : ISelectionFilter
    {
        /// <summary>True for a floor that is not in a group.</summary>
        public bool AllowElement(Element elem) =>
            elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);

        /// <summary>Element picks only; no sub-element references.</summary>
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
