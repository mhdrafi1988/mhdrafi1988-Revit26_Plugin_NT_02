// File: LineStyleOption.cs
// Location: Core/Models/
// Ported from AutoSlopeByPoint V028 — simple Id+Name pair for populating the
// "Line Style" ComboBox from the project's existing OST_Lines subcategories
// (GraphicsStyle elements).

using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models
{
    public class LineStyleOption
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }

        public override string ToString() => Name;
    }
}
