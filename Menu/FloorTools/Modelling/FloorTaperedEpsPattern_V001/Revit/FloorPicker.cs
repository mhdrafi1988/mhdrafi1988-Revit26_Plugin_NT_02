using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>
    /// Entry check 2 — uses a single preselected floor, otherwise prompts for one.
    /// Returns null (with a message) on Esc or when the element is not a floor.
    /// </summary>
    public static class FloorPicker
    {
        public static Floor? Pick(UIDocument uidoc, out string message)
        {
            Document doc = uidoc.Document;
            message = string.Empty;

            ICollection<ElementId> preselected = uidoc.Selection.GetElementIds();
            if (preselected.Count == 1)
            {
                foreach (ElementId id in preselected)
                {
                    if (doc.GetElement(id) is Floor preFloor)
                    {
                        return preFloor;
                    }
                }
            }

            Reference picked;
            try
            {
                picked = uidoc.Selection.PickObject(ObjectType.Element, new FloorSelectionFilter(),
                    "Tapered EPS 2D Pattern: select a floor (Esc to cancel)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                message = "No floor was selected, so the tool was cancelled.";
                return null;
            }

            if (doc.GetElement(picked) is Floor floor)
            {
                return floor;
            }

            message = "The selected element is not a floor. Select a floor and run the tool again.";
            return null;
        }
    }
}
