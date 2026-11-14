using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Infrastructure.Helpers
{
    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (!(elem is Floor floor)) return false;
            if (floor.GroupId != null && floor.GroupId != ElementId.InvalidElementId) return false;
            try
            {
                var slabShapeEditor = floor.GetSlabShapeEditor();
                return slabShapeEditor != null && slabShapeEditor.IsEnabled;
            }
            catch
            {
                return false;
            }
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
