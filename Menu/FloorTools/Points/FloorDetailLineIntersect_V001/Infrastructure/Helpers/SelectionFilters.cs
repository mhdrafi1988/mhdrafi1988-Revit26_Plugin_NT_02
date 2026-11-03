using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Infrastructure.Helpers
{
    /// <summary>Restricts PickObject to floors that are not in a group.</summary>
    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)        => elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    /// <summary>Restricts PickObjects to DetailLine elements only.</summary>
    public class DetailLineSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)        => elem is DetailLine;
        public bool AllowReference(Reference r, XYZ p) => true;
    }
}
