// =======================================================
// File: LineStyleOption.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// New in V026.
// Purpose: Simple Id+Name pair for populating the "Line Style"
//          ComboBox from the project's existing OST_Lines
//          subcategories (GraphicsStyle elements). Kept separate
//          from CircleMarkerGroup since it's a read-only list item,
//          not part of the group's own state.
// =======================================================

using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models
{
    public class LineStyleOption
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }

        public override string ToString() => Name;
    }
}
