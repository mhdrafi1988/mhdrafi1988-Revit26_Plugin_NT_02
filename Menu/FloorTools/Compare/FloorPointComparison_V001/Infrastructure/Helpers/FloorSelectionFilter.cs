using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Infrastructure.Helpers
{
    /// <summary>Restricts element picking to floor elements only.</summary>
    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Floor;

        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
