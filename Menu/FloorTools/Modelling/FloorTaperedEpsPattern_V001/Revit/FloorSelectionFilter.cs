using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Only floors can be picked (footprint and extrusion — extrusion is rejected later with a message).</summary>
    public sealed class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Floor;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
