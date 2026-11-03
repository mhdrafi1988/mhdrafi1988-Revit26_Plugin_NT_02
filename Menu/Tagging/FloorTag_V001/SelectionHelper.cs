using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorTag.V001
{
    public static class SelectionHelper
    {
        public static Floor SelectFloor(UIDocument uiDoc)
        {
            try
            {
                Reference r = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new FloorOnlyFilter(),
                    "Select a Floor");

                return r != null
                    ? uiDoc.Document.GetElement(r) as Floor
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private class FloorOnlyFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)   =>
                elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
            public bool AllowReference(Reference r, XYZ pos) => false;
        }
    }
}
