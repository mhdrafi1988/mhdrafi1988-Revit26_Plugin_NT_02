using System;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Events
{
    /// <summary>Re-reads the picked floor (used by Reset) so edits made while the window is open are picked up.</summary>
    public sealed class RefreshFloorHandler : IExternalEventHandler
    {
        public FloorContext? Current { get; set; }

        public event Action<FloorContext>? Completed;

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            FloorContext? current = Current;
            if (current == null)
            {
                return;
            }

            FloorContext refreshed;
            try
            {
                refreshed = current.Document.IsValidObject
                    ? FloorContextBuilder.Build(current.Document, current.ViewId, current.FloorId)
                    : current;
            }
            catch (Exception)
            {
                refreshed = current;
            }

            Completed?.Invoke(refreshed);
        }

        public string GetName() => "Tapered EPS 2D Pattern – Refresh floor";
    }
}
