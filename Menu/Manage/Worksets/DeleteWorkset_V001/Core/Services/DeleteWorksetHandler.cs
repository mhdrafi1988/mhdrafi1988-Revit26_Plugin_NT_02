using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.DeleteWorkset.V001.Core.Services
{
    /// <summary>
    /// Routes a single queued action from the WPF thread into the Revit API context.
    /// </summary>
    public sealed class DeleteWorksetHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pending;

        /// <summary>Queue one action to be executed when the event fires.</summary>
        public void Queue(Action<UIApplication> action) => _pending = action;

        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            if (action == null) return;

            // Esc / Cancel with a Yes-No confirm while the queued Revit work runs; the service rolls back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.DeleteWorkset, app);
            action.Invoke(app);
        }

        public string GetName() => "Delete Workset V001";
    }
}
