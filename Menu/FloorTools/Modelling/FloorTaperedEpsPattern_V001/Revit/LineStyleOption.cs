using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>A line style (OST_Lines subcategory, projection GraphicsStyle) for a picker.</summary>
    public sealed class LineStyleOption
    {
        public LineStyleOption(string name, ElementId graphicsStyleId)
        {
            Name = name;
            GraphicsStyleId = graphicsStyleId;
        }

        public string Name { get; }
        public ElementId GraphicsStyleId { get; }

        public override string ToString() => Name;
    }
}
