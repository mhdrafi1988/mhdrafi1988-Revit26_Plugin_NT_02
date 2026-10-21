using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V002.Infrastructure.Helpers
{
    /// <summary>Selection filter that lets the user pick footprint roofs only.</summary>
    public sealed class FootprintRoofFilter : ISelectionFilter
    {
        /// <inheritdoc/>
        public bool AllowElement(Element elem) => elem is FootPrintRoof;

        /// <inheritdoc/>
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
