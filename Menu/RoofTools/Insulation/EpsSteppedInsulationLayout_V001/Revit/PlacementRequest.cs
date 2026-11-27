using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit
{
    /// <summary>Everything the placer needs, gathered from the ViewModel.</summary>
    public sealed class PlacementRequest
    {
        public InsulationRoofContext Context { get; set; }
        public LayoutResult Layout { get; set; }
        public ElementId BaseRoofTypeId { get; set; }
        public ElementId StepRoofTypeId { get; set; }
        public InsulationSettings Settings { get; set; }
    }
}
