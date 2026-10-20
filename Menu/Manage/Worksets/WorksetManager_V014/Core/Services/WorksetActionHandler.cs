using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.WorksetManager.V014.Core.Services
{
    public sealed class WorksetActionHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pending;

        public void Queue(Action<UIApplication> action) => _pending = action;

        /// <summary>Runs the queued action through ToolGuard, so an error is logged and shown instead of reaching Revit.</summary>
        public void Execute(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            ToolGuard.RunHandler(GetType(), app, a => action?.Invoke(a));
        }

        public string GetName() => "Workset Manager V012";
    }
}
