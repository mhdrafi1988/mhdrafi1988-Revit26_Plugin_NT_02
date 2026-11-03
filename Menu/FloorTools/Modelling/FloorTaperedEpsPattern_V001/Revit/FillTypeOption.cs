using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>A filled region type for the hatch slot pickers.</summary>
    public sealed class FillTypeOption
    {
        public FillTypeOption(string name, ElementId typeId, string colorHex)
        {
            Name = name;
            TypeId = typeId;
            ColorHex = colorHex;
        }

        /// <summary>Foreground pattern colour of the type ("#RRGGBB"), used for the legend and preview.</summary>
        public string ColorHex { get; }

        public string Name { get; }
        public ElementId TypeId { get; }

        public override string ToString() => Name;
    }
}
