using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.PlanRegionManager.V001.Core.Services
{
    /// <summary>
    /// Routes one queued action from the WPF window into the Revit API context.
    /// </summary>
    public sealed class PlanRegionHandler : IExternalEventHandler
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
            action?.Invoke(app);
        }

        /// <inheritdoc/>
        public string GetName() => "Plan Region Manager V001";
    }
}
