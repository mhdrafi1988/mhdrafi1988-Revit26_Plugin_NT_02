using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit
{
    /// <summary>A roof type for combo-box display, with its compound thickness.</summary>
    public sealed class RoofTypeOption
    {
        public RoofTypeOption(ElementId id, string name, double thicknessMm)
        {
            Id = id;
            Name = name;
            ThicknessMm = thicknessMm;
        }

        public ElementId Id { get; }
        public string Name { get; }
        public double ThicknessMm { get; }

        public string DisplayName => $"{Name}  ({ThicknessMm:0} mm)";
    }
}
