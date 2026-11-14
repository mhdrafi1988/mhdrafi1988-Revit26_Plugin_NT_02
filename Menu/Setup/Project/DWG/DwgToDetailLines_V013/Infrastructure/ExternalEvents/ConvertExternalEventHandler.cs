// ==============================================
// File: ConvertExternalEventHandler.cs
// Layer: Infrastructure/ExternalEvents
// ==============================================

using Autodesk.Revit.UI;
using System;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DwgToDetailLines.V013.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Single orchestrator handler for this tool's one Revit-side action (Convert).
    /// Required because the window is modeless: any Revit API call triggered by a
    /// button click (outside the original IExternalCommand.Execute call) must be
    /// raised through an ExternalEvent, or Revit throws an invalid-API-context error.
    /// </summary>
    public class ConvertExternalEventHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pendingAction;

        public void Raise(ExternalEvent externalEvent, Action<UIApplication> action)
        {
            _pendingAction = action;
            externalEvent.Raise();
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            try
            {
                _pendingAction?.Invoke(app);
            }
            catch (Exception ex)
            {
                Logger.Error("DwgToDetailLines", ex);
            }
            finally
            {
                _pendingAction = null;
            }
        }

        public string GetName() => "DWG to Detail Lines - Convert";
    }
}
