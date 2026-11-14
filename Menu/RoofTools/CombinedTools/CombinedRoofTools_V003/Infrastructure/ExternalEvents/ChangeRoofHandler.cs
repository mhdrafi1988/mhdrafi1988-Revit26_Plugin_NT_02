// =======================================================
// File: ChangeRoofHandler.cs
// Location: Infrastructure/ExternalEvents/
// Re-picks a roof and rebuilds all 5 tab ViewModels for it. Needed
// because the combined window is modeless (Show()) — by the time the
// user clicks "Change Roof", the Command's own Execute() call has long
// since returned, so picking + rebuilding must go through an
// ExternalEvent, same as every other Revit-API-touching action in this
// window (Analyze/Apply/Run on each tab).
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.CombinedRoofTools.V003.Core;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.CombinedRoofTools.V003.Infrastructure.ExternalEvents
{
    public class ChangeRoofHandler : IExternalEventHandler
    {
        public static ChangeRoofPayload Payload;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;
            ChangeRoofPayload current = Payload;

            UIDocument uidoc = app.ActiveUIDocument;

            Reference pickedRef;
            try
            {
                pickedRef = uidoc.Selection.PickObject(
                    ObjectType.Element, new CombinedRoofSelectionFilter(), "Select a RoofBase element");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                current.OnCancelled?.Invoke();
                return;
            }

            if (pickedRef == null)
            {
                current.OnCancelled?.Invoke();
                return;
            }

            RoofBase roof = uidoc.Document.GetElement(pickedRef) as RoofBase;
            if (roof == null)
            {
                current.OnFailed?.Invoke("Selected element is not a RoofBase.");
                return;
            }

            // Esc / Cancel with a Yes-No confirm while the roof is analysed (picking keeps Revit's own Esc).
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.CombinedRoofTools, app);
            try
            {
                var result = CombinedRoofToolsInitializer.BuildAll(app, roof);
                current.OnCompleted?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                current.OnCancelled?.Invoke();
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                current.OnFailed?.Invoke(ex.Message);
            }
        }

        public string GetName() => "Combined Roof Tools — Change Roof Handler";
    }
}
