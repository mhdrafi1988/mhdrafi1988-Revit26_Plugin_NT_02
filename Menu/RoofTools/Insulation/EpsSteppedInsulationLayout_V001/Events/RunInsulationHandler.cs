using System;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Events
{
    /// <summary>
    /// Runs <see cref="InsulationPlacer"/> in Revit's API context. The ViewModel sets
    /// <see cref="Pending"/> and raises the event.
    /// </summary>
    public sealed class RunInsulationHandler : IExternalEventHandler
    {
        public PlacementRequest Pending { get; set; }

        /// <summary>Raised on the UI thread after the placement attempt.</summary>
        public event Action<PlacementResult> Completed;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            PlacementRequest request = Pending;
            Pending = null;

            PlacementResult result;
            try
            {
                result = request == null
                    ? PlacementResult.Failed("Nothing to run.")
                    : InsulationPlacer.Place(request);
            }
            catch (Exception ex)
            {
                result = PlacementResult.Failed("Unexpected error: " + ex.Message);
            }

            Completed?.Invoke(result);
        }

        public string GetName() => "EPS Stepped Insulation – Run";
    }
}
