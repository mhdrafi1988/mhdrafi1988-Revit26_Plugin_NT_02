using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.RoofTag.V018
{
    public static class SelectionHelper
    {
        public static RoofBase SelectRoof(UIDocument uiDoc)
        {
            try
            {
                Reference r = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new RoofOnlyFilter(),
                    "Select a Roof");

                return r != null
                    ? uiDoc.Document.GetElement(r) as RoofBase
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private class RoofOnlyFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)   => elem is RoofBase;
            public bool AllowReference(Reference r, XYZ pos) => false;
        }
    }
}
