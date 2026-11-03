using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorShapeFromLink.V001.Infrastructure.Helpers
{
    /// <summary>Selection filter that lets the user pick floors outside groups only.</summary>
    public sealed class FloorFilter : ISelectionFilter
    {
        /// <inheritdoc/>
        public bool AllowElement(Element elem) =>
            elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);

        /// <inheritdoc/>
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
