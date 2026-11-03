using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>Restricts PickObjects (the "Pick Floors" button) to Floor elements only.</summary>
    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Floor;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
