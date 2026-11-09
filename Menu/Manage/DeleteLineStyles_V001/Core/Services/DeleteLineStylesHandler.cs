using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.DeleteLineStyles.V001.Core.Services
{
    /// <summary>
    /// Routes one queued action from the WPF window into the Revit API context.
    /// </summary>
    public sealed class DeleteLineStylesHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pending;

        /// <summary>Queues the action to run when the external event fires.</summary>
        public void Queue(Action<UIApplication> action) => _pending = action;

        /// <inheritdoc/>
        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            if (action == null) return;

            // Esc / Cancel with a Yes-No confirm while the queued Revit work runs; the service rolls back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.DeleteLineStyles, app);
            action.Invoke(app);
        }

        /// <inheritdoc/>
        public string GetName() => "Delete Line Styles V001";
    }
}
