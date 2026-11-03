// ==================================
// File: FloorSelectionService.cs
// Namespace: Revit26_Plugin_Revised.FloorCreaser.V001
// ==================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;

namespace Revit26_Plugin_Revised.FloorCreaser.V001.Services
{
    /// <summary>
    /// Prompts the user to pick a single floor element.
    /// Throws <see cref="Autodesk.Revit.Exceptions.OperationCanceledException"/>
    /// if the user presses Escape — callers must handle it explicitly.
    /// </summary>
    public class FloorSelectionService
    {
        public Element SelectSingleFloor(UIDocument uiDoc)
        {
            if (uiDoc == null) throw new ArgumentNullException(nameof(uiDoc));

            Reference reference = uiDoc.Selection.PickObject(
                ObjectType.Element,
                new FloorSelectionFilter(),
                "Select one floor");

            return uiDoc.Document.GetElement(reference);
        }

        private sealed class FloorSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
                => elem is Floor; // read-only tool: grouped floors are fine

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
