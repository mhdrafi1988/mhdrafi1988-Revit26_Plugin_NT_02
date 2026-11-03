using Autodesk.Revit.UI;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetManager.V014.Core.Services
{
    public sealed class WorksetActionHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pending;

        public void Queue(Action<UIApplication> action) => _pending = action;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            action?.Invoke(app);
        }

        public string GetName() => "Workset Manager V012";
    }
}
