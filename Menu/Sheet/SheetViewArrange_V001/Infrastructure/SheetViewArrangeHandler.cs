using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.SheetViewArrange.V001.Infrastructure
{
    /// <summary>
    /// Runs one queued action from the modeless window inside Revit's API context.
    /// </summary>
    public sealed class SheetViewArrangeHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pending;

        /// <summary>Queues the action to run the next time the event is raised.</summary>
        public void Queue(Action<UIApplication> action) => _pending = action;

        /// <summary>Revit entry point — guarded so no exception reaches Revit.</summary>
        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            if (action == null) return;

            // Esc / Cancel with a Yes-No confirm while the queued Revit work runs; each service rolls its group back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.SheetViewArrange, app);
            action.Invoke(app);
        }

        /// <summary>Name shown by Revit for this handler.</summary>
        public string GetName() => "Sheet View Arrange V001";
    }
}
